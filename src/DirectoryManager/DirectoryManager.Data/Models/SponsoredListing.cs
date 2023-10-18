﻿using DirectoryManager.Data.Models.BaseModels;

namespace DirectoryManager.Data.Models
{
    public class SponsoredListing : StateInfo
    {
        public int SponsoredListingId { get; set; }

        public DateTime CampaignStartDate { get; set; }

        public DateTime CampaignEndDate { get; set; }

        public int DirectoryEntryId { get; set; }

        public virtual DirectoryEntry? DirectoryEntry { get; set; }

        public int SponsoredListingInvoiceId { get; set; }

        public virtual SponsoredListingInvoice? SponsoredListingInvoice { get; set; }
    }
}