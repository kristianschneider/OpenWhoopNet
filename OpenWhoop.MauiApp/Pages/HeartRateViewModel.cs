using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.MauiApp.Pages
{
    public class HeartRateViewModel
    {
        public DateTime TimestampUtc { get; set; }
        public int Value { get; set; }
        public int? ActivityId { get; set; }
        public int RrCount { get; set; }
    }
}
