using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace DevAtlas.Services
{
    public static class ErrorDialogService
    {
        private static int _isDialogOpen;

        public static void ShowError(string message, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _ = ShowErrorAsync(message, title);
        }

        public static async Task ShowErrorAsync(string message, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var caption = ResolveTitle(title);

            async Task ShowDialog()
            {
                if (Interlocked.Exchange(ref _isDialogOpen, 1) == 1)
                    return;

                try
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(caption, message, ButtonEnum.Ok, Icon.Error);
                    await box.ShowAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _isDialogOpen, 0);
                }
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(ShowDialog);
                return;
            }

            await ShowDialog();
        }

        public static void ShowException(Exception exception, string contextMessage)
        {
            if (exception is OperationCanceledException)
                return;

            var message = string.IsNullOrWhiteSpace(contextMessage)
                ? exception.Message
                : $"{contextMessage}\n\n{exception.Message}";

            ShowError(message);
        }

        public static async Task ShowExceptionAsync(Exception exception, string contextMessage)
        {
            if (exception is OperationCanceledException)
                return;

            var message = string.IsNullOrWhiteSpace(contextMessage)
                ? exception.Message
                : $"{contextMessage}\n\n{exception.Message}";

            await ShowErrorAsync(message);
        }

        private static string ResolveTitle(string? title)
        {
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            try
            {
                return LanguageManager.Instance["MessageError"];
            }
            catch
            {
                return "Error";
            }
        }
    }
}
