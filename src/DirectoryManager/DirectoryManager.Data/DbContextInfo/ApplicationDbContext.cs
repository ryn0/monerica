﻿using DirectoryManager.Data.Models;
using DirectoryManager.Data.Models.BaseModels;
using DirectoryManager.Data.Models.Emails;
using DirectoryManager.Data.Models.SponsoredListings;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace DirectoryManager.Data.DbContextInfo
{
    public class ApplicationDbContext : ApplicationBaseContext<ApplicationDbContext>, IApplicationDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ApplicationUser> ApplicationUser { get; set; }
        public DbSet<ApplicationUserRole> ApplicationUserRole { get; set; }
        public DbSet<BlockedIP> BlockedIPs { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ContentSnippet> ContentSnippets { get; set; }
        public DbSet<DirectoryEntriesAudit> DirectoryEntriesAudit { get; set; }
        public DbSet<DirectoryEntry> DirectoryEntries { get; set; }
        public DbSet<DirectoryEntrySelection> DirectoryEntrySelections { get; set; }
        public DbSet<EmailCampaign> EmailCampaigns { get; set; }
        public DbSet<EmailCampaignMessage> EmailCampaignMessages { get; set; }
        public DbSet<EmailCampaignSubscription> EmailCampaignSubscriptions { get; set; }
        public DbSet<EmailMessage> EmailMessages { get; set; }
        public DbSet<EmailSubscription> EmailSubscriptions { get; set; }
        public DbSet<ExcludeUserAgent> ExcludeUserAgents { get; set; }
        public DbSet<LogEntry> LogEntries { get; set; }
        public DbSet<ProcessorConfig> ProcessorConfigs { get; set; }
        public DbSet<SentEmailRecord> SentEmailRecords { get; set; }
        public DbSet<SponsoredListingOpeningNotification> SponsoredListingOpeningNotifications { get; set; }
        public DbSet<SponsoredListing> SponsoredListings { get; set; }
        public DbSet<SponsoredListingInvoice> SponsoredListingInvoices { get; set; }
        public DbSet<SponsoredListingOffer> SponsoredListingOffers { get; set; }
        public DbSet<SponsoredListingReservation> SponsoredListingReservations { get; set; }
        public DbSet<Subcategory> Subcategories { get; set; }
        public DbSet<Submission> Submissions { get; set; }
        public DbSet<TrafficLog> TrafficLogs { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<DirectoryEntryTag> DirectoryEntryTags { get; set; }

        public DbSet<SearchLog> SearchLogs { get; set; }

        public override int SaveChanges()
        {
            this.SetDates();

            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            this.SetDates();

            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<DirectoryEntry>()
                   .HasIndex(e => new { e.DirectoryEntryId, e.DirectoryStatus })
                   .IsUnique();

            builder.Entity<DirectoryEntry>()
                   .HasIndex(e => e.Link)
                   .IsUnique();

            builder.Entity<DirectoryEntry>()
                   .HasIndex(e => new { e.SubCategoryId, e.DirectoryEntryKey })
                   .IsUnique();

            builder.Entity<DirectoryEntry>()
                    .HasIndex(e => e.SubCategoryId)
                    .HasDatabaseName("IX_DirectoryEntries_SubCategoryId");

            builder.Entity<DirectoryEntryTag>()
                .HasKey(et => new { et.DirectoryEntryId, et.TagId });

            // relationships
            builder.Entity<DirectoryEntryTag>()
                .HasOne(et => et.DirectoryEntry)
                .WithMany(de => de.EntryTags)
                .HasForeignKey(et => et.DirectoryEntryId);

            builder.Entity<DirectoryEntryTag>()
                .HasOne(et => et.Tag)
                .WithMany(t => t.EntryTags)
                .HasForeignKey(et => et.TagId);

            // Tag.Name must be unique
            builder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            builder.Entity<Tag>()
                 .HasIndex(t => t.Key)
                 .IsUnique();

            builder.Entity<Category>()
                   .HasIndex(e => e.CategoryKey)
                   .IsUnique();

            builder.Entity<Subcategory>()
                   .HasIndex(e => new { e.SubCategoryKey, e.CategoryId })
                   .IsUnique();

            builder.Entity<TrafficLog>()
                   .HasIndex(t => t.CreateDate)
                   .HasDatabaseName("IX_TrafficLog_CreateDate");

            builder.Entity<ExcludeUserAgent>()
                   .HasIndex(e => e.UserAgent)
                   .IsUnique();

            builder.Entity<SponsoredListingInvoice>()
                   .HasIndex(e => e.InvoiceId)
                   .IsUnique();

            builder.Entity<SponsoredListingInvoice>()
                   .Property(e => e.Amount)
                   .HasColumnType("decimal(20, 12)");

            builder.Entity<SponsoredListingInvoice>()
                   .Property(e => e.PaidAmount)
                   .HasColumnType("decimal(20, 12)");

            builder.Entity<SponsoredListingInvoice>()
                   .Property(e => e.OutcomeAmount)
                   .HasColumnType("decimal(20, 12)");

            builder.Entity<SponsoredListingOffer>()
                   .Property(e => e.Price)
                   .HasColumnType("decimal(20, 12)");

            builder.Entity<SponsoredListing>()
                   .HasIndex(e => new { e.CreateDate, e.UpdateDate });

            builder.Entity<SponsoredListing>()
                   .HasIndex(e => new { e.CampaignEndDate });

            builder.Entity<SponsoredListing>()
                   .HasOne(sl => sl.DirectoryEntry)
                   .WithMany()
                   .HasForeignKey(sl => sl.DirectoryEntryId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SponsoredListing>()
                   .HasOne(sl => sl.SponsoredListingInvoice)
                   .WithOne(sli => sli.SponsoredListing)
                   .HasForeignKey<SponsoredListing>(sl => sl.SponsoredListingInvoiceId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SponsoredListingOffer>()
                   .HasIndex(e => new { e.SponsorshipType, e.Days, e.CategoryId, e.SubcategoryId })
                   .IsUnique();

            builder.Entity<SponsoredListingOffer>(eb =>
            {
                // 1️⃣ Enforce uniqueness when SubcategoryId IS NULL
                eb.HasIndex(e => new { e.SponsorshipType, e.Days, e.CategoryId })
                  .IsUnique()
                  .HasFilter("[SubcategoryId] IS NULL")
                  .HasDatabaseName("UX_Offer_Type_Days_Cat_NoSubcat");

                // 2️⃣ Enforce uniqueness when SubcategoryId IS NOT NULL
                eb.HasIndex(e => new { e.SponsorshipType, e.Days, e.CategoryId, e.SubcategoryId })
                  .IsUnique()
                  .HasFilter("[SubcategoryId] IS NOT NULL")
                  .HasDatabaseName("UX_Offer_Type_Days_Cat_Subcat");
            });

            builder.Entity<SponsoredListingOffer>()
                   .HasIndex(e => new { e.SponsorshipType, e.Days })
                   .IsUnique()
                   .HasFilter("SubcategoryId IS NULL");

            builder.Entity<ProcessorConfig>()
                   .HasIndex(e => e.PaymentProcessor)
                   .IsUnique();

            builder.Entity<SponsoredListingReservation>()
                   .HasIndex(e => e.ReservationGuid)
                   .IsUnique();

            builder.Entity<SponsoredListingReservation>()
                   .HasIndex(e => e.ExpirationDateTime);

            builder.Entity<EmailMessage>()
                   .HasIndex(e => e.EmailKey)
                   .IsUnique();

            builder.Entity<SentEmailRecord>()
                   .HasIndex(e => new { e.EmailSubscriptionId, e.EmailMessageId })
                   .IsUnique();

            builder.Entity<EmailSubscription>()
                   .HasIndex(e => e.IsSubscribed);

            builder.Entity<EmailSubscription>()
                   .HasIndex(e => e.Email)
                   .IsUnique();

            builder.Entity<EmailCampaignSubscription>()
                    .HasIndex(e => new { e.EmailCampaignId, e.IsActive });

            builder.Entity<EmailCampaignSubscription>()
                   .HasIndex(e => e.EmailSubscriptionId);

            builder.Entity<BlockedIP>()
                   .HasIndex(e => e.IpAddress)
                   .IsUnique();

            builder.Entity<EmailCampaign>()
                   .HasIndex(e => e.EmailCampaignKey)
                   .IsUnique();

            builder.Entity<SponsoredListingOpeningNotification>()
                   .HasIndex(e => new { e.Email, e.SponsorshipType, e.TypeId, e.SubscribedDate })
                   .IsUnique()
                   .HasDatabaseName("IX_SponsoredListingOpeningNotification_Unique");
        }

        private void SetDates()
        {
            foreach (var entry in this.ChangeTracker.Entries()
                .Where(x => (x.Entity is StateInfo) && x.State == EntityState.Added)
                .Select(x => (StateInfo)x.Entity))
            {
                if (entry.CreateDate == DateTime.MinValue)
                {
                    entry.CreateDate = DateTime.UtcNow;
                }
            }

            foreach (var entry in this.ChangeTracker.Entries()
                .Where(x => x.Entity is CreatedStateInfo && x.State == EntityState.Added)
                .Select(x => (CreatedStateInfo)x.Entity)
                .Where(x => x != null))
            {
                if (entry.CreateDate == DateTime.MinValue)
                {
                    entry.CreateDate = DateTime.UtcNow;
                }
            }

            foreach (var entry in this.ChangeTracker.Entries()
                .Where(x => x.Entity is StateInfo && x.State == EntityState.Modified)
                .Select(x => (StateInfo)x.Entity)
                .Where(x => x != null))
            {
                entry.UpdateDate = DateTime.UtcNow;
            }
        }
    }
}