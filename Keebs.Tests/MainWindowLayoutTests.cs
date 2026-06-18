using System.Threading;

namespace Keebs.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void WindowSupportsCompactResizableLayout()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();

                Assert.Equal(390, window.MinWidth);
                Assert.Equal(185, window.MinHeight);
                Assert.Equal(System.Windows.ResizeMode.CanResizeWithGrip, window.ResizeMode);
                Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, window.WindowStyle);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }
}
