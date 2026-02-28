using DirectoryManager.Data.Enums;
using DirectoryManager.Data.Models;
using DirectoryManager.Data.Models.SponsoredListings;
using DirectoryManager.Data.Models.TransferModels;
using DirectoryManager.Data.Repositories.Interfaces;
using DirectoryManager.DisplayFormatting.Helpers;
using DirectoryManager.Utilities.Helpers;
using DirectoryManager.Web.Constants;
using DirectoryManager.Web.Helpers;
using DirectoryManager.Web.Models; // ✅ ListingInventoryModel
using DirectoryManager.Web.Models.Sponsorship;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DirectoryManager.Web.Controllers
{
    [AllowAnonymous]
    [Route("sponsorship")]
    public class SponsorshipController : Controller
    {
        private const int SearchPageSize = 20;
        private const int WaitlistPreviewTake = 10;
        private const int WaitlistPageSize = 25;
        private const int RecentPaidTake = 20;

        private readonly IDirectoryEntryRepository entryRepo;
        private readonly ICategoryRepository categoryRepo;
        private readonly ISubcategoryRepository subcategoryRepo;

        private readonly ISponsoredListingRepository sponsoredListingRepo;
        private readonly ISponsoredListingReservationRepository reservationRepo;
        private readonly ISponsoredListingOfferRepository offerRepo;

        private readonly ISponsoredListingOpeningNotificationRepository waitlistRepo;
        private readonly ISponsoredListingInvoiceRepository invoiceRepo;

        public SponsorshipController(
            IDirectoryEntryRepository entryRepo,
            ICategoryRepository categoryRepo,
            ISubcategoryRepository subcategoryRepo,
            ISponsoredListingRepository sponsoredListingRepo,
            ISponsoredListingReservationRepository reservationRepo,
            ISponsoredListingOfferRepository offerRepo,
            ISponsoredListingOpeningNotificationRepository waitlistRepo,
            ISponsoredListingInvoiceRepository invoiceRepo)
        {
            this.entryRepo = entryRepo;
            this.categoryRepo = categoryRepo;
            this.subcategoryRepo = subcategoryRepo;
            this.sponsoredListingRepo = sponsoredListingRepo;
            this.reservationRepo = reservationRepo;
            this.offerRepo = offerRepo;
            this.waitlistRepo = waitlistRepo;
            this.invoiceRepo = invoiceRepo;
        }

        // GET /sponsorship
        [HttpGet("")]
        public async Task<IActionResult> Index(string? q, int page = 1)
        {
            page = Math.Max(1, page);
            q = (q ?? string.Empty).Trim();

            var vm = new SponsorshipIndexVm
            {
                Query = q,
                Page = page,
                PageSize = SearchPageSize,
                HasSearched = !string.IsNullOrWhiteSpace(q),
            };

            if (vm.HasSearched)
            {
                // Real search (includes tags/category/subcategory/etc)
                var result = await this.entryRepo.SearchAsync(q, page, SearchPageSize);

                vm.TotalCount = result.TotalCount;
                vm.TotalPages = (int)Math.Ceiling(result.TotalCount / (double)SearchPageSize);

                // Use your existing mapper in this controller
                vm.Results = result.Items.Select(this.ToSearchItem).ToList();
            }

            // Waitlist heat (preview on this page)
            vm.WaitlistBoard = await this.BuildWaitlistBoardAsync();

            // Force newest-first (your DTO uses CreateDateUtc -> JoinedUtc in VM)
            if (vm.WaitlistBoard?.MainPreview != null)
            {
                vm.WaitlistBoard.MainPreview = vm.WaitlistBoard.MainPreview
                    .OrderByDescending(x => x.JoinedUtc)
                    .ToList();
            }

            // Recent paid
            vm.RecentPaid = await this.BuildRecentPaidAsync();

            // =====================================
            // ✅ Main Sponsor open + next opening info
            // =====================================
            var maxMain = DirectoryManager.Common.Constants.IntegerConstants.MaxMainSponsoredListings;
            var activeMain = await this.sponsoredListingRepo.GetActiveSponsorsCountAsync(
                SponsorshipType.MainSponsor,
                typeId: null);

            vm.MainSponsorMaxSlots = maxMain;
            vm.MainSponsorActiveCount = activeMain;
            vm.MainSponsorIsOpen = activeMain < maxMain;

            if (!vm.MainSponsorIsOpen)
            {
                var now = DateTime.UtcNow;

                var allActiveMain = await this.sponsoredListingRepo.GetActiveSponsorsByTypeAsync(SponsorshipType.MainSponsor);

                var nextUtc = allActiveMain
                    .Where(x => x.CampaignEndDate > now)
                    .OrderBy(x => x.CampaignEndDate)
                    .Select(x => (DateTime?)x.CampaignEndDate)
                    .FirstOrDefault();

                if (nextUtc.HasValue)
                {
                    vm.MainSponsorInventory = new ListingInventoryModel
                    {
                        NextListingExpiration = nextUtc.Value 
                    };
                }
                else
                {
                    // Fallback (rare) – show something sane
                    vm.MainSponsorInventory = new ListingInventoryModel
                    {
                        NextListingExpiration = now
                    };
                }
            }

            return this.View("Index", vm);
        }

        // POST /sponsorship/select
        [HttpPost("select")]
        [ValidateAntiForgeryToken]
        public IActionResult Select([FromForm] int directoryEntryId)
        {
            return this.RedirectToAction("Options", new { directoryEntryId });
        }

        // GET /sponsorship/options/123
        [HttpGet("options/{directoryEntryId:int}")]
        public async Task<IActionResult> Options(int directoryEntryId, [FromQuery] int subscribed = 0)
        {
            var entry = await this.entryRepo.GetByIdAsync(directoryEntryId);
            if (entry == null)
            {
                return this.NotFound();
            }

            var catId = entry.SubCategory?.CategoryId ?? 0;
            var subId = entry.SubCategoryId;

            var (canAdvertise, reasons) = GetAdvertiseEligibility(entry);

            // Build option panels with availability (but checkout remains in your existing SponsoredListingController)
            var main = await this.BuildTypeOptionAsync(entry, SponsorshipType.MainSponsor, typeIdForScope: null, $"{EnumHelper.GetDescription(SponsorshipType.MainSponsor)} (site-wide)", includeMainSubcategoryCap: true);
            var cat = await this.BuildTypeOptionAsync(entry, SponsorshipType.CategorySponsor, typeIdForScope: catId <= 0 ? null : catId, $"{EnumHelper.GetDescription(SponsorshipType.CategorySponsor)} ({entry.SubCategory?.Category?.Name ?? "Unknown"})");
            var sub = await this.BuildTypeOptionAsync(entry, SponsorshipType.SubcategorySponsor, typeIdForScope: subId <= 0 ? null : subId, $"{EnumHelper.GetDescription(SponsorshipType.SubcategorySponsor)}  ({FormattingHelper.SubcategoryFormatting(entry.SubCategory?.Category?.Name, entry.SubCategory?.Name)})");

            int? pricingSubId = entry.SubCategoryId > 0 ? entry.SubCategoryId : (int?)null;

            main.Offers = await this.LoadOffersAsync(SponsorshipType.MainSponsor, pricingSubId);
            cat.Offers = await this.LoadOffersAsync(SponsorshipType.CategorySponsor, pricingSubId);
            sub.Offers = await this.LoadOffersAsync(SponsorshipType.SubcategorySponsor, pricingSubId);

            // Scoped waitlist previews (jealousy + competition)
            main.Waitlist = await this.BuildWaitlistPanelAsync(SponsorshipType.MainSponsor, typeId: null);
            cat.Waitlist = catId > 0 ? await this.BuildWaitlistPanelAsync(SponsorshipType.CategorySponsor, typeId: catId) : WaitlistPanelVm.Empty("Category waitlist unavailable (missing category).");
            sub.Waitlist = subId > 0 ? await this.BuildWaitlistPanelAsync(SponsorshipType.SubcategorySponsor, typeId: subId) : WaitlistPanelVm.Empty("Subcategory waitlist unavailable (missing subcategory).");

            var vm = new SponsorshipOptionsVm
            {
                DirectoryEntryId = entry.DirectoryEntryId,
                ListingName = entry.Name ?? StringConstants.DefaultName,
                ListingUrl = entry.Link ?? string.Empty,
                DirectoryEntryKey = entry.DirectoryEntryKey ?? string.Empty,
                DirectoryStatus = entry.DirectoryStatus.ToString(),

                CategoryId = catId,
                SubCategoryId = subId,
                CategoryName = entry.SubCategory?.Category?.Name ?? string.Empty,
                SubcategoryName = entry.SubCategory?.Name ?? string.Empty,

                CanAdvertise = canAdvertise,
                IneligibilityReasons = reasons,
                ShowSubscribedBanner = subscribed == 1,

                Main = main,
                Category = cat,
                Subcategory = sub,

                // show global heat under the wizard too
                WaitlistBoard = await this.BuildWaitlistBoardAsync(),
                RecentPaid = await this.BuildRecentPaidAsync(),
            };

            return this.View("Options", vm);
        }

        // GET /sponsorship/waitlist
        [HttpGet("waitlist")]
        public async Task<IActionResult> Waitlist(
            [FromQuery] SponsorshipType type = SponsorshipType.MainSponsor,
            [FromQuery] int? typeId = null,
            [FromQuery] int page = 1)
        {
            // ✅ NO query string at all => show ALL waitlists on one page
            if (this.Request.Query == null || this.Request.Query.Count == 0)
            {
                var overview = await this.BuildWaitlistsOverviewAsync();
                return this.View("Waitlists", overview);
            }

            // =========================
            // Existing single-scope page (paged)
            // =========================
            page = Math.Max(1, page);

            // Validate: MainSponsor uses null typeId
            if (type == SponsorshipType.MainSponsor)
            {
                typeId = null;
            }

            var scopeLabel = await this.GetScopeLabelAsync(type, typeId);
            var paged = await this.waitlistRepo.GetWaitlistPagedAsync(type, typeId, page, WaitlistPageSize);

            // Resolve listing name/url from DirectoryEntryId via repo
            var items = await this.BuildPublicWaitlistItemsAsync(paged.Items);

            var vm = new SponsorshipWaitlistVm
            {
                SponsorshipType = type,
                TypeId = typeId,
                ScopeLabel = scopeLabel,
                TotalCount = paged.TotalCount,
                Items = items
            };

            return this.View("Waitlist", vm);
        }

        [HttpPost("subscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe([FromForm] SponsorshipSubscribeVm vm)
        {
            var entry = await this.entryRepo.GetByIdAsync(vm.DirectoryEntryId);
            if (entry == null)
            {
                return this.NotFound();
            }

            var email = (vm.Email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return this.RedirectToAction("Options", new { directoryEntryId = vm.DirectoryEntryId });
            }

            var catId = entry.SubCategory?.CategoryId;
            var subId = entry.SubCategoryId;

            var scopes = new List<(SponsorshipType Type, int? TypeId)>();

            if (vm.NotifyMain)
            {
                scopes.Add((SponsorshipType.MainSponsor, null));
            }

            if (vm.NotifyCategory && catId.HasValue && catId.Value > 0)
            {
                scopes.Add((SponsorshipType.CategorySponsor, catId.Value));
            }

            if (vm.NotifySubcategory && subId > 0)
            {
                scopes.Add((SponsorshipType.SubcategorySponsor, subId));
            }

            if (scopes.Count == 0)
            {
                return this.RedirectToAction("Options", new { directoryEntryId = entry.DirectoryEntryId });
            }

            // IMPORTANT: pass the non-nullable int id
            await this.waitlistRepo.UpsertManyAsync(email, entry.DirectoryEntryId, scopes);

            return this.RedirectToAction("Options", new { directoryEntryId = entry.DirectoryEntryId, subscribed = 1 });
        }

        private static DirectoryEntry? TryGetEntry(Dictionary<int, DirectoryEntry> lookup, int? id)
        {
            if (id.HasValue && id.Value > 0 && lookup.TryGetValue(id.Value, out var e))
            {
                return e;
            }

            return null;
        }

        private static string GetListingName(DirectoryEntry? entry, int? directoryEntryId)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Name))
            {
                return entry.Name;
            }

            // If we have an ID but couldn't load it (removed/hidden), be explicit.
            if (directoryEntryId.HasValue && directoryEntryId.Value > 0)
            {
                return "Listing unavailable";
            }

            return "Anonymous listing";
        }

        private static (bool canAdvertise, List<string> reasons) GetAdvertiseEligibility(DirectoryEntry e)
        {
            var reasons = new List<string>();

            // Allowed advertising statuses: Admitted/Verified ONLY
            if (e.DirectoryStatus != DirectoryStatus.Admitted && e.DirectoryStatus != DirectoryStatus.Verified)
            {
                reasons.Add($"Status is {e.DirectoryStatus}. Must be Admitted or Verified to advertise.");
            }

            // explicit block statuses
            if (e.DirectoryStatus == DirectoryStatus.Questionable || e.DirectoryStatus == DirectoryStatus.Scam)
            {
                reasons.Add("Listing is marked Questionable/Scam and cannot advertise.");
            }

            // Age gate: verified bypasses
            if (e.DirectoryStatus != DirectoryStatus.Verified)
            {
                if (e.CreateDate == DateTime.MinValue)
                {
                    reasons.Add("Listing age is unknown (missing create date).");
                }
                else
                {
                    var days = (int)Math.Floor((DateTime.UtcNow - e.CreateDate).TotalDays);
                    if (days < DirectoryManager.Web.Constants.IntegerConstants.UnverifiedMinimumDaysListedBeforeAdvertising)
                    {
                        reasons.Add($"Listing is too new: {days} days listed. Needs {DirectoryManager.Web.Constants.IntegerConstants.UnverifiedMinimumDaysListedBeforeAdvertising} days (unless Verified).");
                    }
                }
            }

            return (reasons.Count == 0, reasons);
        }

        private SponsorshipSearchItemVm ToSearchItem(DirectoryEntry e)
        {
            var (ok, reasons) = GetAdvertiseEligibility(e);

            var cat = e.SubCategory?.Category?.Name ?? "";
            var sub = e.SubCategory?.Name ?? "";
            var ageDays = (e.CreateDate == DateTime.MinValue) ? 0 : (int)Math.Floor((DateTime.UtcNow - e.CreateDate).TotalDays);

            return new SponsorshipSearchItemVm
            {
                DirectoryEntryId = e.DirectoryEntryId,
                Name = e.Name ?? StringConstants.DefaultName,
                Link = e.Link ?? "",
                DirectoryEntryKey = e.DirectoryEntryKey ?? "",
                Status = e.DirectoryStatus.ToString(),
                AgeDays = ageDays,
                Category = cat,
                Subcategory = FormattingHelper.SubcategoryFormatting(cat, sub),
                CanAdvertise = ok,
                Reasons = reasons,
            };
        }

        private async Task<List<SponsorshipOfferVm>> LoadOffersAsync(SponsorshipType type, int? subcategoryId)
        {
            var offers = await this.offerRepo.GetByTypeAndSubCategoryAsync(type, subcategoryId);
            if (offers == null || !offers.Any())
            {
                offers = await this.offerRepo.GetByTypeAndSubCategoryAsync(type, null);
            }

            return offers
                .OrderBy(x => x.Days)
                .Select(o => new SponsorshipOfferVm
                {
                    Days = o.Days,
                    PriceUsd = o.Price,
                    Description = o.Description ?? "",
                    PricePerDay = o.Days <= 0 ? 0 : Math.Round(o.Price / o.Days, 2),
                })
                .ToList();
        }

        private async Task<SponsorshipTypeOptionVm> BuildTypeOptionAsync(
            DirectoryEntry entry,
            SponsorshipType type,
            int? typeIdForScope,
            string scopeLabel,
            bool includeMainSubcategoryCap = false)
        {
            // Extension allowed regardless of availability
            var isExtension = await this.sponsoredListingRepo.IsSponsoredListingActive(entry.DirectoryEntryId, type);

            // reservation group rules
            var groupTypeId = type switch
            {
                SponsorshipType.MainSponsor => 0,
                SponsorshipType.CategorySponsor => typeIdForScope ?? 0,
                SponsorshipType.SubcategorySponsor => typeIdForScope ?? 0,
                _ => 0
            };
            var group = ReservationGroupHelper.BuildReservationGroupName(type, groupTypeId);

            // capacity scope rules
            int? capacityTypeId = type == SponsorshipType.MainSponsor ? (int?)null : typeIdForScope;

            var active = await this.sponsoredListingRepo.GetActiveSponsorsCountAsync(type, capacityTypeId);
            var reservations = await this.reservationRepo.GetActiveReservationsCountAsync(group);

            var maxSlots = type switch
            {
                SponsorshipType.MainSponsor => DirectoryManager.Common.Constants.IntegerConstants.MaxMainSponsoredListings,
                SponsorshipType.CategorySponsor => DirectoryManager.Common.Constants.IntegerConstants.MaxCategorySponsoredListings,
                SponsorshipType.SubcategorySponsor => DirectoryManager.Common.Constants.IntegerConstants.MaxSubcategorySponsoredListings,
                _ => 0
            };

            // pool-level check
            var poolAvailable = (active < maxSlots) && (reservations < (maxSlots - active));

            // optional: your main-per-subcategory cap (avoid surprise later)
            bool blockedByMainSubCap = false;
            DateTime? nextMainSubCapOpeningUtc = null;

            if (includeMainSubcategoryCap && type == SponsorshipType.MainSponsor && !isExtension)
            {
                var allActiveMain = await this.sponsoredListingRepo.GetActiveSponsorsByTypeAsync(SponsorshipType.MainSponsor);

                var activeInSameSub = allActiveMain.Count(x =>
                    (x.SubCategoryId.HasValue && x.SubCategoryId.Value == entry.SubCategoryId) ||
                    (x.SubCategoryId == null && x.DirectoryEntry != null && x.DirectoryEntry.SubCategoryId == entry.SubCategoryId));

                if (activeInSameSub >= DirectoryManager.Common.Constants.IntegerConstants.MaxMainSponsorsPerSubcategory)
                {
                    blockedByMainSubCap = true;

                    nextMainSubCapOpeningUtc = allActiveMain
                        .Where(x =>
                            (((x.SubCategoryId.HasValue && x.SubCategoryId.Value == entry.SubCategoryId) ||
                              (x.SubCategoryId == null && x.DirectoryEntry != null && x.DirectoryEntry.SubCategoryId == entry.SubCategoryId))
                             && x.CampaignEndDate > DateTime.UtcNow))
                        .OrderBy(x => x.CampaignEndDate)
                        .Select(x => (DateTime?)x.CampaignEndDate)
                        .FirstOrDefault();
                }
            }

            var isAvailableNow = isExtension || (poolAvailable && !blockedByMainSubCap);
            var allActiveForType = await this.sponsoredListingRepo.GetActiveSponsorsByTypeAsync(type);

            var inScope = FilterActiveSponsorsToScope(allActiveForType, type, typeIdForScope);

            // show next-to-expire first (more useful)
            var ordered = inScope
                .Where(x => x.CampaignEndDate > DateTime.UtcNow)
                .OrderBy(x => x.CampaignEndDate)
                .Take(maxSlots)
                .ToList();

            var activeSlots = ordered.Select(x => new ActiveSponsorSlotVm
            {
                DirectoryEntryId = x.DirectoryEntryId,
                ListingName = !string.IsNullOrWhiteSpace(x.DirectoryEntry?.Name) ? x.DirectoryEntry!.Name! : "Listing",
                ListingUrl = x.DirectoryEntry?.Link ?? "",
                CampaignEndUtc = x.CampaignEndDate,
                IsYou = x.DirectoryEntryId == entry.DirectoryEntryId
            }).ToList();

            var yourUntil = ordered
                .Where(x => x.DirectoryEntryId == entry.DirectoryEntryId)
                .Select(x => (DateTime?)x.CampaignEndDate)
                .FirstOrDefault();

            return new SponsorshipTypeOptionVm
            {
                SponsorshipType = type,
                ScopeLabel = scopeLabel,
                IsExtension = isExtension,
                IsAvailableNow = isAvailableNow,

                PoolActiveCount = active,
                PoolMaxSlots = maxSlots,
                PoolHasCheckoutLock = !isExtension && !poolAvailable && active < maxSlots,

                BlockedByMainSubcategoryCap = blockedByMainSubCap,
                NextOpeningForMainSubcategoryCapUtc = nextMainSubCapOpeningUtc,

                // NEW:
                ActiveSlots = activeSlots,
                YourActiveUntilUtc = yourUntil
            };
        }

        private static IEnumerable<SponsoredListing> FilterActiveSponsorsToScope(
            IEnumerable<SponsoredListing> allActive,
            SponsorshipType type,
            int? typeIdForScope)
        {
            var list = allActive ?? Enumerable.Empty<SponsoredListing>();

            if (type == SponsorshipType.MainSponsor)
            {
                return list;
            }

            if (!typeIdForScope.HasValue || typeIdForScope.Value <= 0)
            {
                return Enumerable.Empty<SponsoredListing>();
            }

            var scopeId = typeIdForScope.Value;

            return type switch
            {
                SponsorshipType.CategorySponsor =>
                    list.Where(x =>
                        (x.CategoryId.HasValue && x.CategoryId.Value == scopeId) ||
                        (x.DirectoryEntry != null &&
                         x.DirectoryEntry.SubCategory != null &&
                         x.DirectoryEntry.SubCategory.CategoryId == scopeId)),

                SponsorshipType.SubcategorySponsor =>
                    list.Where(x =>
                        (x.SubCategoryId.HasValue && x.SubCategoryId.Value == scopeId) ||
                        (x.DirectoryEntry != null && x.DirectoryEntry.SubCategoryId == scopeId)),

                _ => Enumerable.Empty<SponsoredListing>()
            };
        }

        private async Task<WaitlistPanelVm> BuildWaitlistPanelAsync(SponsorshipType type, int? typeId)
        {
            // MainSponsor ignores typeId
            if (type == SponsorshipType.MainSponsor)
            {
                typeId = null;
            }

            var scopeLabel = await this.GetScopeLabelAsync(type, typeId);
            var count = await this.waitlistRepo.GetWaitlistCountAsync(type, typeId);
            var preview = await this.waitlistRepo.GetWaitlistPreviewAsync(type, typeId, WaitlistPreviewTake);

            var publicRows = await this.BuildPublicWaitlistRowsAsync(preview);

            return new WaitlistPanelVm
            {
                ScopeLabel = scopeLabel,
                Count = count,
                Preview = publicRows,
                BrowseUrl = this.Url.Action("Waitlist", "Sponsorship", new { type, typeId }),
                JoinWouldBeRank = count + 1
            };
        }

        private async Task<SponsorshipWaitlistsOverviewVm> BuildWaitlistsOverviewAsync()
        {
            // 3 queries total (one per sponsorship type)
            var mainRows = await this.waitlistRepo.GetWaitlistAllByTypeAsync(SponsorshipType.MainSponsor);
            var catRows = await this.waitlistRepo.GetWaitlistAllByTypeAsync(SponsorshipType.CategorySponsor);
            var subRows = await this.waitlistRepo.GetWaitlistAllByTypeAsync(SponsorshipType.SubcategorySponsor);

            // One DirectoryEntry lookup for EVERYTHING (fast)
            var allIds = mainRows
                .Concat(catRows)
                .Concat(subRows)
                .Select(x => x.DirectoryEntryId)
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var entryLookup = await this.entryRepo.GetByIdsAsync(allIds);

            List<WaitlistPublicItemVm> MapItems(IEnumerable<WaitlistScopedItemDto> rows)
            {
                return rows.Select(dto =>
                {
                    DirectoryEntry? entry = null;

                    if (dto.DirectoryEntryId.HasValue &&
                        dto.DirectoryEntryId.Value > 0 &&
                        entryLookup.TryGetValue(dto.DirectoryEntryId.Value, out var e))
                    {
                        entry = e;
                    }

                    return new WaitlistPublicItemVm
                    {
                        ListingName = GetListingName(entry, dto.DirectoryEntryId),
                        ListingUrl = entry?.Link ?? string.Empty,

                        // ✅ joined = SubscribedDateUtc
                        JoinedUtc = dto.SubscribedDateUtc
                    };
                })
                .OrderByDescending(x => x.JoinedUtc)
                .ThenBy(x => x.ListingName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            }

            // MAIN section (single scope)
            var mainSection = new SponsorshipWaitlistSectionVm
            {
                SponsorshipType = SponsorshipType.MainSponsor,
                TypeId = null,
                ScopeLabel = await this.GetScopeLabelAsync(SponsorshipType.MainSponsor, null),
                TotalCount = mainRows.Count,
                BrowseUrl = this.Url.Action("Waitlist", "Sponsorship", new { type = SponsorshipType.MainSponsor }) ?? "",
                Items = MapItems(mainRows)
            };

            // CATEGORY sections
            var categorySections = new List<SponsorshipWaitlistSectionVm>();
            foreach (var g in catRows
                .Where(x => x.TypeId.HasValue && x.TypeId.Value > 0)
                .GroupBy(x => x.TypeId!.Value))
            {
                var catId = g.Key;
                var label = await this.GetScopeLabelAsync(SponsorshipType.CategorySponsor, catId);

                categorySections.Add(new SponsorshipWaitlistSectionVm
                {
                    SponsorshipType = SponsorshipType.CategorySponsor,
                    TypeId = catId,
                    ScopeLabel = label,
                    TotalCount = g.Count(),
                    BrowseUrl = this.Url.Action("Waitlist", "Sponsorship", new { type = SponsorshipType.CategorySponsor, typeId = catId }) ?? "",
                    Items = MapItems(g)
                });
            }

            categorySections = categorySections
                .OrderBy(x => x.ScopeLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // SUBCATEGORY sections
            var subcategorySections = new List<SponsorshipWaitlistSectionVm>();
            foreach (var g in subRows
                .Where(x => x.TypeId.HasValue && x.TypeId.Value > 0)
                .GroupBy(x => x.TypeId!.Value))
            {
                var subId = g.Key;
                var label = await this.GetScopeLabelAsync(SponsorshipType.SubcategorySponsor, subId);

                subcategorySections.Add(new SponsorshipWaitlistSectionVm
                {
                    SponsorshipType = SponsorshipType.SubcategorySponsor,
                    TypeId = subId,
                    ScopeLabel = label,
                    TotalCount = g.Count(),
                    BrowseUrl = this.Url.Action("Waitlist", "Sponsorship", new { type = SponsorshipType.SubcategorySponsor, typeId = subId }) ?? "",
                    Items = MapItems(g)
                });
            }

            subcategorySections = subcategorySections
                .OrderBy(x => x.ScopeLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SponsorshipWaitlistsOverviewVm
            {
                TotalCount = mainSection.TotalCount + categorySections.Sum(x => x.TotalCount) + subcategorySections.Sum(x => x.TotalCount),
                Main = mainSection,
                Categories = categorySections,
                Subcategories = subcategorySections
            };
        }

        private async Task<RecentPaidVm> BuildRecentPaidAsync()
        {
            var rows = await this.invoiceRepo.GetRecentPaidActivePurchasesAsync(RecentPaidTake);

            // ✅ one lookup for all entries so we can build category/subcategory links
            var ids = rows
                .Select(x => x.DirectoryEntryId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            var entryLookup = await this.entryRepo.GetByIdsAsync(ids);

            DirectoryEntry? TryGet(int id)
                => (id > 0 && entryLookup.TryGetValue(id, out var e)) ? e : null;

            return new RecentPaidVm
            {
                Items = rows.Select(r =>
                {
                    var entry = TryGet(r.DirectoryEntryId);

                    return new RecentPaidItemVm
                    {
                        PaidUtc = r.PaidDateUtc,

                        SponsorshipTypeEnum = r.SponsorshipType,
                        SponsorshipType = EnumHelper.GetDescription(r.SponsorshipType),

                        DirectoryEntryId = r.DirectoryEntryId,
                        PlacementUrl = BuildPlacementUrl(r.SponsorshipType, entry),

                        Days = r.Days,
                        AmountUsd = r.AmountUsd,
                        PricePerDayUsd = r.PricePerDayUsd,

                        PaidCurrency = r.PaidCurrency.ToString(),
                        PaidAmount = r.PaidAmount,

                        ExpiresUtc = r.ExpiresUtc,

                        ListingName = r.ListingName,
                        ListingUrl = r.ListingUrl
                    };
                }).ToList()
            };
        }

        // ✅ Add this helper anywhere inside the controller class
        private static string BuildPlacementUrl(SponsorshipType type, DirectoryEntry? entry)
        {
            // Main Sponsor => home
            if (type == SponsorshipType.MainSponsor)
            {
                return "/";
            }

            // Category/Subcategory => resolve from the entry
            // Adjust these key property names ONLY if yours differ:
            var catKey = entry?.SubCategory?.Category?.CategoryKey;     // e.g. "exchanges"
            var subKey = entry?.SubCategory?.SubCategoryKey;           // e.g. "instant-swaps"

            if (type == SponsorshipType.CategorySponsor)
            {
                return !string.IsNullOrWhiteSpace(catKey) ? $"/{catKey}" : "/";
            }

            if (type == SponsorshipType.SubcategorySponsor)
            {
                // common pattern: /{categoryKey}/{subCategoryKey}
                if (!string.IsNullOrWhiteSpace(catKey) && !string.IsNullOrWhiteSpace(subKey))
                {
                    return $"/{catKey}/{subKey}";
                }

                // fallback if your site uses /{subKey}
                if (!string.IsNullOrWhiteSpace(subKey))
                {
                    return $"/{subKey}";
                }

                return "/";
            }

            return "/";
        }

        private async Task<string> GetScopeLabelAsync(SponsorshipType type, int? typeId)
        {
            if (type == SponsorshipType.MainSponsor)
            {
                return EnumHelper.GetDescription(SponsorshipType.MainSponsor) + " (site-wide)";
            }

            if (type == SponsorshipType.CategorySponsor)
            {
                if (typeId.HasValue && typeId.Value > 0)
                {
                    var cat = await this.categoryRepo.GetByIdAsync(typeId.Value);
                    return cat != null ? $"{EnumHelper.GetDescription(SponsorshipType.CategorySponsor)}: {cat.Name}" : EnumHelper.GetDescription(SponsorshipType.CategorySponsor);
                }

                return EnumHelper.GetDescription(SponsorshipType.CategorySponsor);
            }

            if (type == SponsorshipType.SubcategorySponsor)
            {
                if (typeId.HasValue && typeId.Value > 0)
                {
                    var sub = await this.subcategoryRepo.GetByIdAsync(typeId.Value);
                    if (sub != null)
                    {
                        var cat = sub.Category ?? await this.categoryRepo.GetByIdAsync(sub.CategoryId);
                        return $"{EnumHelper.GetDescription(SponsorshipType.SubcategorySponsor)}: {FormattingHelper.SubcategoryFormatting(cat?.Name, sub.Name)}";
                    }
                }

                return EnumHelper.GetDescription(SponsorshipType.SubcategorySponsor);
            }

            return "Sponsorship";
        }

        private async Task<List<WaitlistPublicRowVm>> BuildPublicWaitlistRowsAsync(IEnumerable<WaitlistItemDto> dtos)
        {
            var list = (dtos ?? Enumerable.Empty<WaitlistItemDto>()).ToList();
            var lookup = await this.LoadDirectoryEntryLookupAsync(list.Select(x => x.DirectoryEntryId));

            return list.Select(dto =>
            {
                var entry = TryGetEntry(lookup, dto.DirectoryEntryId);

                return new WaitlistPublicRowVm
                {
                    ListingName = GetListingName(entry, dto.DirectoryEntryId),
                    ListingUrl = entry?.Link ?? string.Empty,
                    JoinedUtc = dto.CreateDateUtc
                };
            }).ToList();
        }

        private async Task<List<WaitlistPublicItemVm>> BuildPublicWaitlistItemsAsync(IEnumerable<WaitlistItemDto> dtos)
        {
            var list = (dtos ?? Enumerable.Empty<WaitlistItemDto>()).ToList();
            var lookup = await this.LoadDirectoryEntryLookupAsync(list.Select(x => x.DirectoryEntryId));

            return list.Select(dto =>
            {
                var entry = TryGetEntry(lookup, dto.DirectoryEntryId);

                return new WaitlistPublicItemVm
                {
                    ListingName = GetListingName(entry, dto.DirectoryEntryId),
                    ListingUrl = entry?.Link ?? string.Empty,
                    JoinedUtc = dto.CreateDateUtc
                };
            }).ToList();
        }

        private async Task<Dictionary<int, DirectoryEntry>> LoadDirectoryEntryLookupAsync(IEnumerable<int?> directoryEntryIds)
        {
            var ids = (directoryEntryIds ?? Enumerable.Empty<int?>())
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            // ✅ single query instead of N queries
            return await this.entryRepo.GetByIdsAsync(ids);
        }

        private async Task<List<WaitlistPreviewRowVm>> BuildWaitlistPreviewRowsAsync(IEnumerable<WaitlistItemDto> dtos)
        {
            var list = (dtos ?? Enumerable.Empty<WaitlistItemDto>()).ToList();

            // Newest first (per your requirement)
            list = list
                .OrderByDescending(x => x.CreateDateUtc)
                .ThenByDescending(x => x.SponsoredListingOpeningNotificationId)
                .ToList();

            var lookup = await this.LoadDirectoryEntryLookupAsync(list.Select(x => x.DirectoryEntryId));

            return list.Select(dto =>
            {
                var entry = TryGetEntry(lookup, dto.DirectoryEntryId);

                return new WaitlistPreviewRowVm
                {
                    ListingName = GetListingName(entry, dto.DirectoryEntryId),
                    ListingUrl = entry?.Link ?? string.Empty,
                    JoinedUtc = dto.CreateDateUtc
                };
            }).ToList();
        }

        private async Task<WaitlistBoardVm> BuildWaitlistBoardAsync()
        {
            var mainCount = await this.waitlistRepo.GetWaitlistCountAsync(SponsorshipType.MainSponsor, null);

            var mainPreview = await this.waitlistRepo.GetWaitlistPreviewAsync(
                SponsorshipType.MainSponsor, null, WaitlistPreviewTake);

            var previewRows = await this.BuildWaitlistPreviewRowsAsync(mainPreview);

            return new WaitlistBoardVm
            {
                MainWaitlistCount = mainCount,
                MainPreview = previewRows,
                BrowseWaitlistUrl = this.Url.Action("Waitlist", "Sponsorship")
            };
        }
    }
}