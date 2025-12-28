namespace UltramarineCli.Models
{
    internal class QueueSettings
    {
        public string QueueName { get; set; } = "ultramarine-tasks";
        public int MaxDeliveryAttempts { get; set; } = 5;
    }
}
