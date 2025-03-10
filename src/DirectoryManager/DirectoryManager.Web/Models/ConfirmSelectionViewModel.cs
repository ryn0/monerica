﻿using DirectoryManager.DisplayFormatting.Models;

namespace DirectoryManager.Web.Models
{
    public class ConfirmSelectionViewModel
    {
        required public DirectoryEntryViewModel SelectedDirectoryEntry { get; set; }
        required public SponsoredListingOfferModel Offer { get; set; }
        public bool CanCreateSponsoredListing { get; set; } = true;
        public DateTime NextListingExpiration { get; set; }
        public bool IsExtension { get; set; }
    }
}