namespace UltramarineCli.Models
{
    internal class RouteEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public List<string> Methods { get; set; } = new();
        public RouterAuthConfig Auth { get; set; } = new();
    }
}
