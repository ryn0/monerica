﻿namespace DirectoryManager.Web.Models
{
    public class SponsoredListingHomeModel
    {
        public bool CanCreateMainListing { get; set; }
        public DateTime NextListingExpiration { get; set; }
        public int CurrentListingCount { get; set; }
        public Dictionary<int, string> AvailableSubCatetgories { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> UnavailableSubCatetgories { get; set; } = new Dictionary<int, string>();
        public string? Message { get; set; }
        public Dictionary<int, string> AvailableCategories { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> UnavailableCategories { get; set; } = new Dictionary<int, string>();
    }
}