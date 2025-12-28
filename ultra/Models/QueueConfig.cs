namespace UltramarineCli.Models
{
    internal class QueueConfig
    {
        public QueueSettings ProjectSettings { get; set; } = new();
        public ConnectionInfo Local { get; set; } = new();
        public ConnectionInfo Production { get; set; } = new();
    }
}
