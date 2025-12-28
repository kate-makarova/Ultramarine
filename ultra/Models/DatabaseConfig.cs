namespace UltramarineCli.Models
{
    internal class DatabaseConfig
    {
        public ProjectSettings ProjectSettings { get; set; } = new();
        public ConnectionInfo Local { get; set; } = new();
        public ConnectionInfo Production { get; set; } = new();
    }
}
