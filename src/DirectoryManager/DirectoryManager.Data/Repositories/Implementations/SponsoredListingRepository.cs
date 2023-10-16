﻿using DirectoryManager.Data.DbContextInfo;
using DirectoryManager.Data.Models;
using DirectoryManager.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DirectoryManager.Data.Repositories.Implementations
{
    public class SponsoredListingRepository : ISponsoredListingRepository
    {
        private readonly IApplicationDbContext context;

        public SponsoredListingRepository(IApplicationDbContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<SponsoredListing?> GetByIdAsync(int id)
        {
            return await this.context.SponsoredListings.FindAsync(id);
        }

        public async Task<SponsoredListing?> GetByInvoiceIdAsync(int sponsoredListingInvoiceId)
        {
            return await this.context.SponsoredListings
                                     .FirstOrDefaultAsync(x => x.SponsoredListingInvoiceId == sponsoredListingInvoiceId);
        }

        public async Task<IEnumerable<SponsoredListing>> GetAllAsync()
        {
            return await this.context.SponsoredListings.ToListAsync();
        }

        public async Task<SponsoredListing> CreateAsync(SponsoredListing sponsoredListing)
        {
            await this.context.SponsoredListings.AddAsync(sponsoredListing);
            await this.context.SaveChangesAsync();

            return sponsoredListing;
        }

        public async Task<bool> UpdateAsync(SponsoredListing sponsoredListing)
        {
            try
            {
                this.context.SponsoredListings.Update(sponsoredListing);
                await this.context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task DeleteAsync(int id)
        {
            var sponsoredListing = await this.GetByIdAsync(id);
            if (sponsoredListing != null)
            {
                this.context.SponsoredListings.Remove(sponsoredListing);
                await this.context.SaveChangesAsync();
            }
        }
    }
}