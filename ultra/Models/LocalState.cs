using System;
using System.Collections.Generic;
using System.Text;

namespace UltramarineCli.Models
{
    internal class LocalState
    {
        public bool DatabaseProvisioned { get; set; } = false;
        public bool QueueProvisioned { get; set; } = false;
    }
}
