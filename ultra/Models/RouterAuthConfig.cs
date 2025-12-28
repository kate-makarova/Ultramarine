namespace UltramarineCli.Models
{
    internal class RouterAuthConfig
    {
        public bool Required { get; set; }
        public List<string> Privileges { get; set; } = new();
    }
}
