using System.Text;

namespace Keebs;

internal sealed class TextSession
{
    private readonly StringBuilder _currentWord = new();
    private readonly Queue<string> _previousWords = new();
    private int _boundaryCharactersAfterPreviousWord;
    private bool _needsWordBoundaryBeforeNextWord;

    public PredictionContext Context => new(
        _currentWord.ToString(),
        _previousWords.ToArray());

    public bool NeedsWordBoundaryBeforeNextWord => _needsWordBoundaryBeforeNextWord;

    /// <summary>
    /// Bumped by every local edit so an in-flight accessibility read can tell
    /// that its text predates the session it is about to overwrite.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// The word the caret sits in for a block of text, using the same parsing
    /// rules as <see cref="SeedFromTextBeforeCaret"/>.
    /// </summary>
    public static string ParseCurrentWord(string textBeforeCaret)
    {
        var word = new StringBuilder();

        for (var index = textBeforeCaret.Length - 1; index >= 0; index--)
        {
            var normalizedCharacter = Normalize(textBeforeCaret[index]);
            if (!char.IsLetter(normalizedCharacter) && normalizedCharacter != '\'')
            {
                break;
            }

            word.Insert(0, normalizedCharacter);
        }

        return word.ToString();
    }

    public void SeedFromTextBeforeCaret(string textBeforeCaret)
    {
        _currentWord.Clear();
        _previousWords.Clear();
        _boundaryCharactersAfterPreviousWord = 0;
        _needsWordBoundaryBeforeNextWord = false;

        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var character in textBeforeCaret)
        {
            var normalizedCharacter = Normalize(character);
            if (char.IsLetter(normalizedCharacter) || normalizedCharacter == '\'')
            {
                word.Append(normalizedCharacter);
                _boundaryCharactersAfterPreviousWord = 0;
                _needsWordBoundaryBeforeNextWord = false;
                continue;
            }

            AddParsedWord(words, word);
            if (words.Count > 0)
            {
                _boundaryCharactersAfterPreviousWord++;
                _needsWordBoundaryBeforeNextWord = !char.IsWhiteSpace(character);
            }
        }

        var caretIsInWord = word.Length > 0;
        if (caretIsInWord)
        {
            _currentWord.Append(word.ToString());
            _boundaryCharactersAfterPreviousWord = 0;
            _needsWordBoundaryBeforeNextWord = false;
        }

        foreach (var previousWord in words.TakeLast(4))
        {
            _previousWords.Enqueue(previousWord);
        }
    }

    public IReadOnlyList<TextCommit> TypeText(string text)
    {
        var commits = new List<TextCommit>();

        if (text.Length > 0)
        {
            Revision++;
        }

        foreach (var character in text)
        {
            var normalizedCharacter = Normalize(character);
            if (char.IsLetter(normalizedCharacter) || normalizedCharacter == '\'')
            {
                _currentWord.Append(normalizedCharacter);
                _boundaryCharactersAfterPreviousWord = 0;
                _needsWordBoundaryBeforeNextWord = false;
            }
            else
            {
                if (CommitBoundary() is { } commit)
                {
                    commits.Add(commit);
                    _needsWordBoundaryBeforeNextWord = !char.IsWhiteSpace(character);
                }
                else if (_previousWords.Count > 0)
                {
                    _boundaryCharactersAfterPreviousWord++;
                    _needsWordBoundaryBeforeNextWord = !char.IsWhiteSpace(character);
                }
            }
        }

        return commits;
    }

    public void Backspace()
    {
        Revision++;

        if (_currentWord.Length > 0)
        {
            _currentWord.Remove(_currentWord.Length - 1, 1);
            _needsWordBoundaryBeforeNextWord = false;
            return;
        }

        if (_boundaryCharactersAfterPreviousWord > 1)
        {
            _boundaryCharactersAfterPreviousWord--;
            _needsWordBoundaryBeforeNextWord = false;
            return;
        }

        if (_previousWords.Count == 0)
        {
            return;
        }

        var previousWords = _previousWords.ToList();
        var wordToRestore = previousWords[^1];
        previousWords.RemoveAt(previousWords.Count - 1);

        _previousWords.Clear();
        foreach (var previousWord in previousWords)
        {
            _previousWords.Enqueue(previousWord);
        }

        _currentWord.Append(wordToRestore);
        _boundaryCharactersAfterPreviousWord = 0;
        _needsWordBoundaryBeforeNextWord = false;
    }

    public void BackspaceWord()
    {
        Revision++;

        if (_currentWord.Length > 0)
        {
            _currentWord.Clear();
            _needsWordBoundaryBeforeNextWord = false;
            return;
        }

        if (_previousWords.Count == 0)
        {
            _boundaryCharactersAfterPreviousWord = 0;
            _needsWordBoundaryBeforeNextWord = false;
            return;
        }

        var previousWords = _previousWords.ToList();
        previousWords.RemoveAt(previousWords.Count - 1);

        _previousWords.Clear();
        foreach (var previousWord in previousWords)
        {
            _previousWords.Enqueue(previousWord);
        }

        _boundaryCharactersAfterPreviousWord = previousWords.Count > 0 ? 1 : 0;
        _needsWordBoundaryBeforeNextWord = false;
    }

    public TextCommit? CommitBoundary()
    {
        if (_currentWord.Length == 0)
        {
            return null;
        }

        Revision++;

        var word = _currentWord.ToString();
        var previousWord = _previousWords.Count == 0 ? string.Empty : _previousWords.Last();
        _previousWords.Enqueue(word);
        _boundaryCharactersAfterPreviousWord = 1;
        while (_previousWords.Count > 4)
        {
            _previousWords.Dequeue();
        }

        _currentWord.Clear();
        _needsWordBoundaryBeforeNextWord = false;
        return new TextCommit(word, previousWord);
    }

    public SuggestionReplacement AcceptSuggestion(string suggestion)
    {
        Revision++;

        var typedLength = _currentWord.Length;

        _currentWord.Clear();
        _currentWord.Append(suggestion.ToLowerInvariant());
        CommitBoundary();
        _needsWordBoundaryBeforeNextWord = false;

        return new SuggestionReplacement(typedLength, $"{suggestion} ");
    }

    public void ResetPredictionContext()
    {
        Revision++;
        _currentWord.Clear();
        _previousWords.Clear();
        _boundaryCharactersAfterPreviousWord = 0;
        _needsWordBoundaryBeforeNextWord = false;
    }

    private static char Normalize(char character)
    {
        return character == '’' ? '\'' : char.ToLowerInvariant(character);
    }

    private static void AddParsedWord(List<string> words, StringBuilder word)
    {
        if (word.Length == 0)
        {
            return;
        }

        words.Add(word.ToString());
        word.Clear();
    }

}

internal sealed record TextCommit(string Word, string PreviousWord);
