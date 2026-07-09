namespace StreamFlow.Core.AudioHandling;
public class Subscription : IDisposable
{
    public string SubscriberName;
    public IObserver<TrackPlayerEventArgs> Observer
    { get; set; }

    public Subscription(IObserver<TrackPlayerEventArgs> observer, string subscriberName)
    {
        SubscriberName = subscriberName;
        Observer = observer;
    }

    public void Inform(object sender, TrackPlayerEventArgs e)
    {
        Observer.OnNext(e);
    }


    public void Dispose()
    {
        TrackPlayer.Unsubscribe(this);
    }
}
