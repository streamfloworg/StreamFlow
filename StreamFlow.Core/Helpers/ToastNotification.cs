using Microsoft.Toolkit.Uwp.Notifications;

namespace StreamFlow.Core.Helpers;
internal class ToastNotification
{
    public static Task<ToastContentBuilder> NewToast(string message, string title, string ?audio = null)
    {
        ToastContentBuilder builder;
        if (audio != null)
        {
            builder = new ToastContentBuilder().AddText(title).AddText(message).AddAudio(new Uri(audio));
        }
        else
        {
            builder = new ToastContentBuilder().AddText(title).AddText(message);
        }
        return Task.FromResult(builder);
    }
}
