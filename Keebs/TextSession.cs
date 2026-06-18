using System.Text;

namespace Keebs;

internal sealed class TextSession
{
    private readonly StringBuilder _currentWord = new();
    private readonly Queue<string> _previousWords = new();

    public PredictionContext Context => new(
        _currentWord.ToString(),
        _previousWords.ToArray());

    public void TypeText(string text)
    {
        foreach (var character in text)
        {
            if (char.IsLetter(character) || character == '\'')
            {
                _currentWord.Append(char.ToLowerInvariant(character));
            }
            else
            {
                CommitBoundary();
            }
        }
    }

    public void Backspace()
    {
        if (_currentWord.Length > 0)
        {
            _currentWord.Remove(_currentWord.Length - 1, 1);
        }
    }

    public void CommitBoundary()
    {
        if (_currentWord.Length == 0)
        {
            return;
        }

        _previousWords.Enqueue(_currentWord.ToString());
        while (_previousWords.Count > 4)
        {
            _previousWords.Dequeue();
        }

        _currentWord.Clear();
    }

    public SuggestionReplacement AcceptSuggestion(string suggestion)
    {
        var typedLength = _currentWord.Length;

        _currentWord.Clear();
        _currentWord.Append(suggestion.ToLowerInvariant());
        CommitBoundary();

        return new SuggestionReplacement(typedLength, $"{suggestion} ");
    }

    public void ResetPredictionContext()
    {
        _currentWord.Clear();
    }
}
