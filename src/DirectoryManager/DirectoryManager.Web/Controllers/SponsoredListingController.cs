﻿using System.Text;
using DirectoryManager.Data.Enums;
using DirectoryManager.Data.Models;
using DirectoryManager.Data.Models.SponsoredListings;
using DirectoryManager.Data.Repositories.Interfaces;
using DirectoryManager.DisplayFormatting.Helpers;
using DirectoryManager.DisplayFormatting.Models;
using DirectoryManager.Utilities.Helpers;
using DirectoryManager.Web.Constants;
using DirectoryManager.Web.Helpers;
using DirectoryManager.Web.Models;
using DirectoryManager.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using NowPayments.API.Interfaces;
using NowPayments.API.Models;

namespace DirectoryManager.Web.Controllers
{
    public class SponsoredListingController : BaseController
    {
        private readonly ISubcategoryRepository subCategoryRepository;
        private readonly ICategoryRepository categoryRepository;
        private readonly IDirectoryEntryRepository directoryEntryRepository;
        private readonly ISponsoredListingRepository sponsoredListingRepository;
        private readonly ISponsoredListingInvoiceRepository sponsoredListingInvoiceRepository;
        private readonly INowPaymentsService paymentService;
        private readonly IMemoryCache cache;
        private readonly ISponsoredListingOfferRepository sponsoredListingOfferRepository;
        private readonly ISponsoredListingReservationRepository sponsoredListingReservationRepository;
        private readonly IBlockedIPRepository blockedIPRepository;
        private readonly ICacheService cacheService;
        private readonly ILogger<SponsoredListingController> logger;

        public SponsoredListingController(
            ISubcategoryRepository subCategoryRepository,
            ICategoryRepository categoryRepository,
            IDirectoryEntryRepository directoryEntryRepository,
            ISponsoredListingRepository sponsoredListingRepository,
            ISponsoredListingInvoiceRepository sponsoredListingInvoiceRepository,
            ITrafficLogRepository trafficLogRepository,
            INowPaymentsService paymentService,
            IUserAgentCacheService userAgentCacheService,
            IMemoryCache cache,
            ISponsoredListingOfferRepository sponsoredListings,
            ISponsoredListingReservationRepository sponsoredListingReservationRepository,
            IBlockedIPRepository blockedIPRepository,
            ICacheService cacheService,
            ILogger<SponsoredListingController> logger)
            : base(trafficLogRepository, userAgentCacheService, cache)
        {
            this.subCategoryRepository = subCategoryRepository;
            this.categoryRepository = categoryRepository;
            this.directoryEntryRepository = directoryEntryRepository;
            this.sponsoredListingRepository = sponsoredListingRepository;
            this.sponsoredListingInvoiceRepository = sponsoredListingInvoiceRepository;
            this.paymentService = paymentService;
            this.cache = cache;
            this.sponsoredListingOfferRepository = sponsoredListings;
            this.sponsoredListingReservationRepository = sponsoredListingReservationRepository;
            this.cacheService = cacheService;
            this.blockedIPRepository = blockedIPRepository;
            this.logger = logger;
        }

        [Route("advertising")]
        [Route("sponsoredlisting")]
        public async Task<IActionResult> IndexAsync()
        {
            var mainSponsorType = SponsorshipType.MainSponsor;
            var mainSponsorReserverationGroup = ReservationGroupHelper.BuildReservationGroupName(mainSponsorType, 0);
            var currentMainSponsorListings = await this.sponsoredListingRepository.GetActiveSponsorsByTypeAsync(mainSponsorType);
            var model = new SponsoredListingHomeModel();

            if (currentMainSponsorListings != null && currentMainSponsorListings.Any())
            {
                var count = currentMainSponsorListings.Count();

                model.CurrentListingCount = count;

                if (count >= Common.Constants.IntegerConstants.MaxMainSponsoredListings)
                {
                    // max listings reached
                    model.CanCreateMainListing = false;
                }
                else
                {
                    var totalActiveListings = await this.sponsoredListingRepository
                                                        .GetActiveSponsorsCountAsync(mainSponsorType, null);
                    var totalActiveReservations = await this.sponsoredListingReservationRepository
                                                            .GetActiveReservationsCountAsync(mainSponsorReserverationGroup);

                    if (CanPurchaseListing(totalActiveListings, totalActiveReservations, mainSponsorType))
                    {
                        model.CanCreateMainListing = true;
                    }
                    else
                    {
                        model.Message = StringConstants.CheckoutInProcess;
                        model.CanCreateMainListing = false;
                    }
                }

                // Get the next listing expiration date (i.e., the soonest CampaignEndDate)
                model.NextListingExpiration = currentMainSponsorListings.Min(x => x.CampaignEndDate);
            }
            else
            {
                model.CanCreateMainListing = true;
            }

            var allActiveSubcategories = await this.subCategoryRepository.GetAllActiveSubCategoriesAsync(Common.Constants.IntegerConstants.MinimumSponsoredActiveSubcategories);
            var currentSubCategorySponsorListings = await this.sponsoredListingRepository
                                                              .GetActiveSponsorsByTypeAsync(SponsorshipType.SubcategorySponsor);

            if (currentSubCategorySponsorListings != null)
            {
                foreach (var subcategory in allActiveSubcategories)
                {
                    if (currentSubCategorySponsorListings.FirstOrDefault(x => x.SubCategoryId == subcategory.SubCategoryId) == null)
                    {
                        model.AvailableSubCatetgories.Add(subcategory.SubCategoryId, FormattingHelper.SubcategoryFormatting(subcategory.Category.Name, subcategory.Name));
                    }
                    else
                    {
                        model.UnavailableSubCatetgories.Add(subcategory.SubCategoryId, FormattingHelper.SubcategoryFormatting(subcategory.Category.Name, subcategory.Name));
                    }
                }

                model.AvailableSubCatetgories = model.AvailableSubCatetgories.OrderBy(x => x.Value).ToDictionary<int, string>();
                model.UnavailableSubCatetgories = model.UnavailableSubCatetgories.OrderBy(x => x.Value).ToDictionary<int, string>();
            }

            return this.View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("sponsoredlisting/selectlisting")]
        public async Task<IActionResult> SelectListing(
            SponsorshipType sponsorshipType = SponsorshipType.MainSponsor,
            int? subCategoryId = null)
        {
            var totalActiveListings = await this.sponsoredListingRepository
                                                .GetActiveSponsorsCountAsync(sponsorshipType, subCategoryId);

            if (sponsorshipType == SponsorshipType.SubcategorySponsor)
            {
                if (subCategoryId != null)
                {
                    var totalActiveEntriesInCategory = await this.directoryEntryRepository
                                                                 .GetActiveEntriesByCategoryAsync(subCategoryId.Value);

                    this.ViewBag.CanAdvertise =
                            totalActiveListings < Common.Constants.IntegerConstants.MaxSubCategorySponsoredListings &&
                            totalActiveEntriesInCategory.Count() >= Common.Constants.IntegerConstants.MinimumSponsoredActiveSubcategories;
                }
            }

            this.ViewBag.SponsorshipType = sponsorshipType;

            IEnumerable<DirectoryEntry> entries = await this.FilterEntries(subCategoryId);

            return this.View("SelectListing", entries);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("sponsoredlisting/selectduration")]
        public async Task<IActionResult> SelectDurationAsync(
            int directoryEntryId,
            SponsorshipType sponsorshipType)
        {
            if (sponsorshipType == SponsorshipType.Unknown)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidSponsorshipType });
            }

            var directoryEntry = await this.directoryEntryRepository.GetByIdAsync(directoryEntryId);

            if (directoryEntry == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidSelection });
            }

            if (!this.IsOldEnough(directoryEntry))
            {
                return this.BadRequest(new { Error = $"Unverfied listing must be listed for at least {IntegerConstants.UnverifiedMinimumDaysListedBeforeAdvertising} days before advertisting." });
            }

            int? subCategoryId = null;

            if (directoryEntry != null)
            {
                subCategoryId = directoryEntry.SubCategoryId;
            }

            var currentListings = await this.sponsoredListingRepository.GetActiveSponsorsByTypeAsync(sponsorshipType);
            var isCurrentSponsor = currentListings?.FirstOrDefault(x => x.DirectoryEntryId == directoryEntryId) != null;
            var totalActiveListings = await this.sponsoredListingRepository
                                                .GetActiveSponsorsCountAsync(sponsorshipType, subCategoryId);
            if (currentListings != null)
            {
                if (!isCurrentSponsor && !CanAdvertise(sponsorshipType, totalActiveListings))
                {
                    return this.BadRequest(new { Error = StringConstants.MaximumNumberOfSponsorsReached });
                }
            }

            this.ViewBag.Subcategory = FormattingHelper.SubcategoryFormatting(directoryEntry?.SubCategory?.Category.Name, directoryEntry?.SubCategory?.Name);
            this.ViewBag.SubCategoryId = directoryEntry?.SubCategoryId;
            this.ViewBag.DirectoryEntryId = directoryEntryId;
            this.ViewBag.SponsorshipType = sponsorshipType;

            var model = await this.GetListingDurations(sponsorshipType, subCategoryId);

            return this.View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("sponsoredlisting/selectduration")]
        public async Task<IActionResult> SelectDurationAsync(
            int directoryEntryId,
            int selectedOfferId)
        {
            var selectedOffer = await this.sponsoredListingOfferRepository.GetByIdAsync(selectedOfferId);

            if (selectedOffer == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidOfferSelection });
            }

            var directoryEntry = await this.directoryEntryRepository.GetByIdAsync(directoryEntryId);

            if (directoryEntry == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidListing });
            }

            var reservationGroup = ReservationGroupHelper.BuildReservationGroupName(selectedOffer.SponsorshipType, directoryEntry.SubCategoryId);
            var isActiveSponsor = await this.sponsoredListingRepository.IsSponsoredListingActive(directoryEntryId, selectedOffer.SponsorshipType);
            int? sponsorshipSubCategoryId = null;

            if (selectedOffer.SponsorshipType == SponsorshipType.SubcategorySponsor)
            {
                sponsorshipSubCategoryId = directoryEntry.SubCategoryId;
            }

            var totalActiveListings = await this.sponsoredListingRepository
                                                .GetActiveSponsorsCountAsync(selectedOffer.SponsorshipType, sponsorshipSubCategoryId);
            var totalActiveReservations = await this.sponsoredListingReservationRepository
                                                    .GetActiveReservationsCountAsync(reservationGroup);

            if (!CanPurchaseListing(
                    totalActiveListings, totalActiveReservations, selectedOffer.SponsorshipType) && !isActiveSponsor)
            {
                return this.BadRequest(new { Error = string.Format(StringConstants.CheckoutInProcess, IntegerConstants.ReservationMinutes) });
            }

            var reservationExpirationDate = DateTime.UtcNow.AddMinutes(IntegerConstants.ReservationMinutes);
            var reservation = await this.sponsoredListingReservationRepository
                                        .CreateReservationAsync(reservationExpirationDate, reservationGroup);

            return this.RedirectToAction(
                        "ConfirmNowPayments",
                        new
                        {
                            directoryEntryId = directoryEntryId,
                            selectedOfferId = selectedOfferId,
                            rsvId = reservation.ReservationGuid
                        });
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("sponsoredlisting/subcategoryselection")]
        public async Task<IActionResult> SubCategory(
            int subCategoryId,
            Guid? rsvId = null)
        {
            var sponsorshipType = SponsorshipType.SubcategorySponsor;

            if (rsvId == null)
            {
                var totalActiveListings = await this.sponsoredListingRepository
                                                    .GetActiveSponsorsCountAsync(sponsorshipType, subCategoryId);
                var reservationGroup = ReservationGroupHelper.BuildReservationGroupName(sponsorshipType, subCategoryId);
                var totalActiveReservations = await this.sponsoredListingReservationRepository
                                                        .GetActiveReservationsCountAsync(reservationGroup);

                if (!CanPurchaseListing(totalActiveListings, totalActiveReservations, sponsorshipType))
                {
                    return this.BadRequest(new { Error = string.Format(StringConstants.CheckoutInProcess, IntegerConstants.ReservationMinutes) });
                }

                var reservationExpirationDate = DateTime.UtcNow.AddMinutes(IntegerConstants.ReservationMinutes);
                var reservation = await this.sponsoredListingReservationRepository
                                            .CreateReservationAsync(reservationExpirationDate, reservationGroup);
                var guid = reservation.ReservationGuid;

                // Redirect back to the same page with the new reservation ID
                return this.RedirectToAction("SubCategorySelection", new { subCategoryId = subCategoryId, rsvId = guid });
            }
            else
            {
                var existingReservation = await this.sponsoredListingReservationRepository.GetReservationByGuidAsync(rsvId.Value);

                if (existingReservation == null)
                {
                    return this.BadRequest(new { Error = string.Format(StringConstants.CheckoutInProcess, IntegerConstants.ReservationMinutes) });
                }
            }

            this.ViewBag.ReservationGuid = rsvId;

            IEnumerable<DirectoryEntry> entries = await this.FilterEntries(subCategoryId);

            return this.View("SubCategorySelection", entries);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("sponsoredlisting/confirmnowpayments")]
        public async Task<IActionResult> ConfirmNowPaymentsAsync(
            int directoryEntryId,
            int selectedOfferId,
            Guid? rsvId = null)
        {
            var offer = await this.sponsoredListingOfferRepository.GetByIdAsync(selectedOfferId);

            if (offer == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidOfferSelection });
            }

            var directoryEntry = await this.directoryEntryRepository.GetByIdAsync(directoryEntryId);

            if (directoryEntry == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidSelection });
            }

            var totalActiveListings = await this.sponsoredListingRepository
                                                .GetActiveSponsorsCountAsync(offer.SponsorshipType, directoryEntry.SubCategoryId);

            if (rsvId == null)
            {
                var reservationGroup = ReservationGroupHelper.BuildReservationGroupName(offer.SponsorshipType, directoryEntry.SubCategoryId);
                var isActiveSponsor = await this.sponsoredListingRepository.IsSponsoredListingActive(directoryEntryId, offer.SponsorshipType);
                var totalActiveReservations = await this.sponsoredListingReservationRepository
                                                        .GetActiveReservationsCountAsync(reservationGroup);

                if (!CanPurchaseListing(totalActiveListings, totalActiveReservations, offer.SponsorshipType) && !isActiveSponsor)
                {
                    return this.BadRequest(new { Error = string.Format(StringConstants.CheckoutInProcess, IntegerConstants.ReservationMinutes) });
                }
            }
            else
            {
                var existingReservation = await this.sponsoredListingReservationRepository.GetReservationByGuidAsync(rsvId.Value);

                if (existingReservation == null)
                {
                    return this.BadRequest(new { Error = StringConstants.ErrorWithCheckoutProcess });
                }
            }

            this.ViewBag.ReservationGuid = rsvId;

            var link2Name = this.cacheService.GetSnippet(SiteConfigSetting.Link2Name);
            var link3Name = this.cacheService.GetSnippet(SiteConfigSetting.Link3Name);
            var currentListings = await this.sponsoredListingRepository.GetActiveSponsorsByTypeAsync(offer.SponsorshipType);
            var viewModel = GetConfirmationModel(offer, directoryEntry, link2Name, link3Name, currentListings);
            var isCurrentSponsor = currentListings?.FirstOrDefault(x => x.DirectoryEntryId == directoryEntryId) != null;

            if (currentListings != null && currentListings.Any() && !isCurrentSponsor)
            {
                viewModel.CanCreateSponsoredListing = CanAdvertise(offer.SponsorshipType, totalActiveListings);

                // Get the next listing expiration date (i.e., the soonest CampaignEndDate)
                viewModel.NextListingExpiration = currentListings.Min(x => x.CampaignEndDate);
            }
            else
            {
                viewModel.CanCreateSponsoredListing = true;
            }

            return this.View(viewModel);
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("sponsoredlisting/confirmnowpayments")]
        public async Task<IActionResult> ConfirmedNowPaymentsAsync(
            int directoryEntryId,
            int selectedOfferId,
            Guid? rsvId = null,
            string? email = null)
        {
            var ipAddress = this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

            if (this.blockedIPRepository.IsBlockedIp(ipAddress))
            {
                return this.NotFound();
            }

            var sponsoredListingOffer = await this.sponsoredListingOfferRepository.GetByIdAsync(selectedOfferId);

            if (sponsoredListingOffer == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvalidOfferSelection });
            }

            var directoryEntry = await this.directoryEntryRepository.GetByIdAsync(directoryEntryId);

            if (directoryEntry == null)
            {
                return this.BadRequest(new { Error = StringConstants.DirectoryEntryNotFound });
            }

            if (rsvId == null)
            {
                var isActiveSponsor = await this.sponsoredListingRepository.IsSponsoredListingActive(directoryEntryId, sponsoredListingOffer.SponsorshipType);
                var totalActiveListings = await this.sponsoredListingRepository
                                                    .GetActiveSponsorsCountAsync(
                                                        sponsoredListingOffer.SponsorshipType,
                                                        directoryEntry.SubCategoryId);
                var reservationGroup = ReservationGroupHelper.BuildReservationGroupName(
                                                                    sponsoredListingOffer.SponsorshipType,
                                                                    directoryEntry.SubCategoryId);
                var totalActiveReservations = await this.sponsoredListingReservationRepository
                                                        .GetActiveReservationsCountAsync(reservationGroup);

                if (!CanPurchaseListing(
                    totalActiveListings, totalActiveReservations, sponsoredListingOffer.SponsorshipType) && !isActiveSponsor)
                {
                    return this.BadRequest(new { Error = string.Format(StringConstants.CheckoutInProcess, IntegerConstants.ReservationMinutes) });
                }
            }
            else
            {
                var existingReservation = await this.sponsoredListingReservationRepository.GetReservationByGuidAsync(rsvId.Value);

                if (existingReservation == null)
                {
                    return this.BadRequest(new { Error = StringConstants.ErrorWithCheckoutProcess });
                }

                var existingInvoice = await this.sponsoredListingInvoiceRepository.GetByReservationGuidAsync(existingReservation.ReservationGuid);

                if (existingInvoice != null)
                {
                    return this.BadRequest(new { Error = StringConstants.InvoiceAlreadyCreated });
                }
            }

            this.ViewBag.ReservationGuid = rsvId;
            var existingListing = await this.sponsoredListingRepository.GetActiveSponsorAsync(directoryEntryId, sponsoredListingOffer.SponsorshipType);
            var startDate = DateTime.UtcNow;

            if (existingListing != null)
            {
                startDate = existingListing.CampaignEndDate;
            }

            var invoice = await this.CreateInvoice(directoryEntry, sponsoredListingOffer, startDate, ipAddress);
            var invoiceRequest = this.GetInvoiceRequest(sponsoredListingOffer, invoice);

            this.paymentService.SetDefaultUrls(invoiceRequest);

            var invoiceFromProcessor = await this.paymentService.CreateInvoice(invoiceRequest);

            if (invoiceFromProcessor == null)
            {
                return this.BadRequest(new { Error = "Failed to create invoice." });
            }

            if (invoiceFromProcessor.Id == null)
            {
                return this.BadRequest(new { Error = "Failed to create invoice ID." });
            }

            invoice.ReservationGuid = (rsvId == null) ? Guid.Empty : rsvId.Value;
            invoice.ProcessorInvoiceId = invoiceFromProcessor.Id;
            invoice.PaymentProcessor = PaymentProcessor.NOWPayments;
            invoice.InvoiceRequest = JsonConvert.SerializeObject(invoiceRequest);
            invoice.InvoiceResponse = JsonConvert.SerializeObject(invoiceFromProcessor);
            invoice.Email = InputHelper.SetEmail(email);

            await this.sponsoredListingInvoiceRepository.UpdateAsync(invoice);

            if (string.IsNullOrWhiteSpace(invoiceFromProcessor?.InvoiceUrl))
            {
                return this.BadRequest(new { Error = "Failed to get invoice URL." });
            }

            return this.Redirect(invoiceFromProcessor.InvoiceUrl);
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("sponsoredlisting/nowpaymentscallback")]
        public async Task<IActionResult> NowPaymentsCallBackAsync()
        {
            using var reader = new StreamReader(this.Request.Body, Encoding.UTF8);
            var callbackPayload = await reader.ReadToEndAsync();

            this.logger.LogError(callbackPayload);

            IpnPaymentMessage? ipnMessage = null;
            try
            {
                ipnMessage = JsonConvert.DeserializeObject<IpnPaymentMessage>(callbackPayload);
            }
            catch (JsonException)
            {
                return this.BadRequest(new { Error = "Failed to parse the request body." });
            }

            if (ipnMessage == null)
            {
                return this.BadRequest(new { Error = StringConstants.DeserializationObjectIsNull });
            }

            var nowPaymentsSig = this.Request
                                     .Headers[NowPayments.API.Constants.StringConstants.HeaderNameAuthCallBack]
                                     .FirstOrDefault() ?? string.Empty;

            bool isValidRequest = this.paymentService.IsIpnRequestValid(
                callbackPayload,
                nowPaymentsSig,
                out string errorMsg);

            if (!isValidRequest)
            {
                return this.BadRequest(new { Error = errorMsg });
            }

            if (ipnMessage == null)
            {
                return this.BadRequest(new { Error = StringConstants.DeserializationObjectIsNull });
            }

            if (ipnMessage.OrderId == null)
            {
                return this.BadRequest(new { Error = "Order ID is null." });
            }

            var invoice = await this.sponsoredListingInvoiceRepository
                                    .GetByInvoiceIdAsync(Guid.Parse(ipnMessage.OrderId));

            if (invoice == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvoiceNotFound });
            }

            invoice.PaymentResponse = JsonConvert.SerializeObject(ipnMessage);
            invoice.PaidAmount = ipnMessage.PayAmount;
            invoice.OutcomeAmount = ipnMessage.OutcomeAmount;

            if (ipnMessage == null)
            {
                return this.BadRequest(new { Error = StringConstants.DeserializationObjectIsNull });
            }

            if (ipnMessage.PaymentStatus == null)
            {
                return this.BadRequest(new { Error = "Payment status is null." });
            }

            var processorPaymentStatus = EnumHelper.ParseStringToEnum<NowPayments.API.Enums.PaymentStatus>(ipnMessage.PaymentStatus);
            var translatedValue = ConvertToInternalStatus(processorPaymentStatus);
            invoice.PaymentStatus = translatedValue;

            if (ipnMessage.PayCurrency == null)
            {
                return this.BadRequest(new { Error = "Pay currency is null." });
            }

            var processorCurrency = EnumHelper.ParseStringToEnum<Currency>(ipnMessage.PayCurrency);
            invoice.PaidInCurrency = processorCurrency;

            await this.CreateNewSponsoredListing(invoice);

            return this.Ok();
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("sponsoredlisting/nowpaymentssuccess")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "StyleCop.CSharp.NamingRules",
                "SA1313:Parameter names should begin with lower-case letter",
                Justification = "This is the param from them")]
        public async Task<IActionResult> NowPaymentsSuccess([FromQuery] string NP_id)
        {
            var processorInvoice = await this.paymentService.GetPaymentStatusAsync(NP_id);

            if (processorInvoice == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvoiceNotFound });
            }

            if (processorInvoice.OrderId == null)
            {
                return this.BadRequest(new { Error = "Order ID not found." });
            }

            var existingInvoice = await this.sponsoredListingInvoiceRepository
                                            .GetByInvoiceIdAsync(Guid.Parse(processorInvoice.OrderId));

            if (existingInvoice == null)
            {
                return this.BadRequest(new { Error = StringConstants.InvoiceNotFound });
            }

            existingInvoice.PaymentStatus = PaymentStatus.Paid;
            existingInvoice.PaymentResponse = NP_id;

            await this.CreateNewSponsoredListing(existingInvoice);

            var viewModel = new SuccessViewModel
            {
                OrderId = existingInvoice.InvoiceId,
                ListingEndDate = existingInvoice.CampaignEndDate
            };

            return this.View("NowPaymentsSuccess", viewModel);
        }

        [Route("sponsoredlisting/edit/{sponsoredListingId}")]
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> EditAsync(int sponsoredListingId)
        {
            var listing = await this.sponsoredListingRepository.GetByIdAsync(sponsoredListingId);
            if (listing == null)
            {
                return this.NotFound();
            }

            var directoryEntry = await this.directoryEntryRepository.GetByIdAsync(listing.DirectoryEntryId);

            var model = new EditListingViewModel
            {
                Id = listing.SponsoredListingId,
                CampaignStartDate = listing.CampaignStartDate,
                CampaignEndDate = listing.CampaignEndDate,
                SponsorshipType = listing.SponsorshipType,
                Name = directoryEntry.Name
            };

            return this.View(model);
        }

        [Route("sponsoredlisting/edit/{sponsoredListingId}")]
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditAsync(int sponsoredListingId, EditListingViewModel model)
        {
            if (!this.ModelState.IsValid)
            {
                return this.View(model);
            }

            var listing = await this.sponsoredListingRepository.GetByIdAsync(sponsoredListingId);
            if (listing == null)
            {
                return this.NotFound();
            }

            listing.CampaignStartDate = model.CampaignStartDate;
            listing.CampaignEndDate = model.CampaignEndDate;

            await this.sponsoredListingRepository.UpdateAsync(listing);

            this.cache.Remove(StringConstants.EntriesCacheKey);
            this.cache.Remove(StringConstants.SponsoredListingsCacheKey);

            return this.RedirectToAction("List");
        }

        [AllowAnonymous]
        [Route("sponsoredlisting/offers")]
        [HttpGet]
        public async Task<IActionResult> Offers()
        {
            // Retrieve all MainSponsor offers and include the Subcategory and Category navigation properties
            var mainSponsorshipOffers = await this.sponsoredListingOfferRepository
                .GetAllByTypeAsync(SponsorshipType.MainSponsor);

            // Map, filter, and order the main sponsorship offers
            var enabledMainSponsorshipOffers = mainSponsorshipOffers
                .Select(o => new SponsoredListingOfferDisplayModel
                {
                    Description = o.Description,
                    Days = o.Days,
                    PriceCurrency = o.PriceCurrency,
                    Price = o.Price,
                    SponsorshipType = o.SponsorshipType,
                    CategorySubcategory = o.Subcategory != null
                        ? FormattingHelper.SubcategoryFormatting(o.Subcategory.Category?.Name ?? StringConstants.Default, o.Subcategory.Name)
                        : StringConstants.Default
                })
                .OrderBy(o => o.CategorySubcategory == StringConstants.Default ? 0 : 1) // Entries with no Subcategory come first
                .ThenBy(o => o.CategorySubcategory)
                .ThenBy(o => o.Days)
                .ToList();

            // Retrieve all SubcategorySponsor offers and include the Subcategory and Category navigation properties
            var subCategorySponsorshipOffers = await this.sponsoredListingOfferRepository
                .GetAllByTypeAsync(SponsorshipType.SubcategorySponsor);

            // Map, filter, and order the subcategory sponsorship offers
            var enabledSubCategoryOffers = subCategorySponsorshipOffers
                .Select(o => new SponsoredListingOfferDisplayModel
                {
                    Description = o.Description,
                    Days = o.Days,
                    PriceCurrency = o.PriceCurrency,
                    Price = o.Price,
                    SponsorshipType = o.SponsorshipType,
                    CategorySubcategory = o.Subcategory != null
                        ? FormattingHelper.SubcategoryFormatting(o.Subcategory.Category?.Name ?? StringConstants.Default, o.Subcategory.Name)
                        : StringConstants.Default
                })
                .OrderBy(o => o.CategorySubcategory == StringConstants.Default ? 0 : 1) // Entries with no Subcategory come first
                .ThenBy(o => o.CategorySubcategory)
                .ThenBy(o => o.Days)
                .ToList();

            // Pass the data to the view using a strongly typed model
            var model = new SponsoredListingOffersViewModel
            {
                MainSponsorshipOffers = enabledMainSponsorshipOffers,
                SubCategorySponsorshipOffers = enabledSubCategoryOffers
            };

            return this.View(model);
        }

        [AllowAnonymous]
        [Route("sponsoredlisting/current")]
        [HttpGet]
        public IActionResult Current()
        {
            return this.View();
        }

        [Route("sponsoredlisting/activelistings")]
        [HttpGet]
        public async Task<IActionResult> ActiveListings()
        {
            var listings = await this.sponsoredListingRepository.GetAllActiveSponsorsAsync();

            // Filter listings by type
            var mainSponsorListings = listings.Where(l => l.SponsorshipType == SponsorshipType.MainSponsor).ToList();
            var subCategorySponsorListings = listings.Where(l => l.SponsorshipType == SponsorshipType.SubcategorySponsor).ToList();

            var model = new ActiveSponsoredListingViewModel
            {
                MainSponsorItems = mainSponsorListings.Select(listing => new ActiveSponsoredListingModel
                {
                    ListingName = listing.DirectoryEntry?.Name ?? StringConstants.DefaultName,
                    SponsoredListingId = listing.SponsoredListingId,
                    CampaignEndDate = listing.CampaignEndDate,
                    ListingUrl = listing.DirectoryEntry?.Link ?? string.Empty,
                    DirectoryListingId = listing.DirectoryEntryId,
                    SponsorshipType = listing.SponsorshipType
                }).ToList(),

                SubCategorySponsorItems = subCategorySponsorListings.Select(listing => new ActiveSponsoredListingModel
                {
                    ListingName = listing.DirectoryEntry?.Name ?? StringConstants.DefaultName,
                    SponsoredListingId = listing.SponsoredListingId,
                    CampaignEndDate = listing.CampaignEndDate,
                    ListingUrl = listing.DirectoryEntry?.Link ?? string.Empty,
                    DirectoryListingId = listing.DirectoryEntryId,
                    SubcategoryName = this.SetSubcategoryNameAsync(listing.SubCategoryId),
                    SponsorshipType = listing.SponsorshipType
                }).ToList()
            };

            return this.View("activelistings", model);
        }

        [Route("sponsoredlisting/list/{page?}")]
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> List(int page = 1)
        {
            int pageSize = IntegerConstants.DefaultPageSize;
            var totalListings = await this.sponsoredListingRepository.GetTotalCountAsync();
            var listings = await this.sponsoredListingRepository.GetPaginatedListingsAsync(page, pageSize);

            var model = new PaginatedListingsViewModel
            {
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(totalListings / (double)pageSize),
                Listings = listings.Select(l => new ListingViewModel
                {
                    Id = l.SponsoredListingId,
                    DirectoryEntryName = l.DirectoryEntry?.Name ?? StringConstants.DefaultName,
                    SponsorshipType = l.SponsorshipType,
                    StartDate = l.CampaignStartDate,
                    EndDate = l.CampaignEndDate
                }).ToList()
            };

            return this.View(model);
        }

        private static ConfirmSelectionViewModel GetConfirmationModel(
            SponsoredListingOffer offer,
            DirectoryEntry directoryEntry,
            string link2Name,
            string link3Name,
            IEnumerable<SponsoredListing> currentListings)
        {
            return new ConfirmSelectionViewModel
            {
                SelectedDirectoryEntry = new DirectoryEntryViewModel()
                {
                    CreateDate = directoryEntry.CreateDate,
                    UpdateDate = directoryEntry.UpdateDate,
                    ItemDisplayType = DisplayFormatting.Enums.ItemDisplayType.Normal,
                    DateOption = DisplayFormatting.Enums.DateDisplayOption.NotDisplayed,
                    IsSponsored = false,
                    Link2Name = link2Name,
                    Link3Name = link3Name,
                    Link = directoryEntry.Link,
                    Name = directoryEntry.Name,
                    DirectoryEntryKey = directoryEntry.DirectoryEntryKey,
                    Contact = directoryEntry.Contact,
                    Description = directoryEntry.Description,
                    DirectoryEntryId = directoryEntry.DirectoryEntryId,
                    DirectoryStatus = directoryEntry.DirectoryStatus,
                    Link2 = directoryEntry.Link2,
                    Link3 = directoryEntry.Link3,
                    Location = directoryEntry.Location,
                    Note = directoryEntry.Note,
                    Processor = directoryEntry.Processor,
                    SubCategoryId = directoryEntry.SubCategoryId
                },
                Offer = new SponsoredListingOfferModel()
                {
                    Description = offer.Description,
                    Days = offer.Days,
                    SponsoredListingOfferId = offer.SponsoredListingOfferId,
                    USDPrice = offer.Price,
                    SponsorshipType = offer.SponsorshipType
                },
                IsExtension = currentListings.FirstOrDefault(x => x.DirectoryEntryId == directoryEntry.DirectoryEntryId) != null
            };
        }

        private static PaymentStatus ConvertToInternalStatus(
            NowPayments.API.Enums.PaymentStatus externalStatus)
        {
            return externalStatus switch
            {
                NowPayments.API.Enums.PaymentStatus.Unknown => PaymentStatus.Unknown,
                NowPayments.API.Enums.PaymentStatus.Waiting => PaymentStatus.InvoiceCreated,
                NowPayments.API.Enums.PaymentStatus.Sending or
                    NowPayments.API.Enums.PaymentStatus.Confirming or
                    NowPayments.API.Enums.PaymentStatus.Confirmed => PaymentStatus.Pending,
                NowPayments.API.Enums.PaymentStatus.Finished => PaymentStatus.Paid,
                NowPayments.API.Enums.PaymentStatus.PartiallyPaid => PaymentStatus.UnderPayment,
                NowPayments.API.Enums.PaymentStatus.Failed or
                    NowPayments.API.Enums.PaymentStatus.Refunded => PaymentStatus.Failed,
                NowPayments.API.Enums.PaymentStatus.Expired => PaymentStatus.Expired,
                _ => throw new ArgumentOutOfRangeException(nameof(externalStatus), externalStatus, null),
            };
        }

        private static bool CanAdvertise(
            SponsorshipType sponsorshipType,
            int totalForTypeInGroup)
        {
            if (sponsorshipType == SponsorshipType.MainSponsor)
            {
                return totalForTypeInGroup < Common.Constants.IntegerConstants.MaxMainSponsoredListings;
            }

            if (sponsorshipType == SponsorshipType.SubcategorySponsor)
            {
                return totalForTypeInGroup < DirectoryManager.Common.Constants.IntegerConstants.MaxSubCategorySponsoredListings;
            }

            throw new InvalidOperationException("SponsorshipType:" + sponsorshipType.ToString());
        }

        private static bool CanPurchaseListing(
                int totalActiveListings,
                int totalActiveReservations,
                SponsorshipType sponsorshipType)
        {
            if (sponsorshipType == SponsorshipType.MainSponsor)
            {
                return (totalActiveListings <= Common.Constants.IntegerConstants.MaxMainSponsoredListings) &&
                       (totalActiveReservations < (Common.Constants.IntegerConstants.MaxMainSponsoredListings - totalActiveListings));
            }

            if (sponsorshipType == SponsorshipType.SubcategorySponsor)
            {
                return (totalActiveListings <= Common.Constants.IntegerConstants.MaxSubCategorySponsoredListings) &&
                       (totalActiveReservations < (Common.Constants.IntegerConstants.MaxSubCategorySponsoredListings - totalActiveListings));
            }

            throw new NotImplementedException("SponsorshipType:" + sponsorshipType.ToString());
        }

        private string SetSubcategoryNameAsync(int? subCategoryId)
        {
            if (subCategoryId == null)
            {
                return string.Empty;
            }

            var subcategory = this.subCategoryRepository.GetByIdAsync(subCategoryId.Value).Result;

            if (subcategory == null)
            {
                return string.Empty;
            }

            var category = this.categoryRepository.GetByIdAsync(subcategory.CategoryId).Result;

            if (category == null)
            {
                return string.Empty;
            }

            return FormattingHelper.SubcategoryFormatting(category.Name, subcategory.Name);
        }

        private PaymentRequest GetInvoiceRequest(
            SponsoredListingOffer sponsoredListingOffer,
            SponsoredListingInvoice invoice)
        {
            return new PaymentRequest
            {
                IsFeePaidByUser = true,
                PriceAmount = sponsoredListingOffer.Price,
                PriceCurrency = this.paymentService.PriceCurrency,
                PayCurrency = this.paymentService.PayCurrency,
                OrderId = invoice.InvoiceId.ToString(),
                OrderDescription = sponsoredListingOffer.Description
            };
        }

        private async Task<SponsoredListingInvoice> CreateInvoice(
            DirectoryEntry directoryEntry,
            SponsoredListingOffer sponsoredListingOffer,
            DateTime startDate,
            string ipAddress)
        {
            return await this.sponsoredListingInvoiceRepository.CreateAsync(
                new SponsoredListingInvoice
                {
                    DirectoryEntryId = directoryEntry.DirectoryEntryId,
                    Currency = Currency.USD,
                    InvoiceId = Guid.NewGuid(),
                    PaymentStatus = PaymentStatus.InvoiceCreated,
                    CampaignStartDate = startDate,
                    CampaignEndDate = startDate.AddDays(sponsoredListingOffer.Days),
                    Amount = sponsoredListingOffer.Price,
                    InvoiceDescription = sponsoredListingOffer.Description,
                    SponsorshipType = sponsoredListingOffer.SponsorshipType,
                    SubCategoryId = directoryEntry.SubCategoryId,
                    IpAddress = ipAddress
                });
        }

        private async Task<List<SponsoredListingOfferModel>> GetListingDurations(SponsorshipType sponsorshipType, int? subcategoryId)
        {
            var offers = await this.sponsoredListingOfferRepository.GetByTypeAndSubCategoryAsync(sponsorshipType, subcategoryId);
            var model = new List<SponsoredListingOfferModel>();

            foreach (var offer in offers.OrderBy(x => x.Days))
            {
                model.Add(new SponsoredListingOfferModel
                {
                    SponsoredListingOfferId = offer.SponsoredListingOfferId,
                    Description = offer.Description,
                    Days = offer.Days,
                    USDPrice = offer.Price
                });
            }

            return model;
        }

        private async Task CreateNewSponsoredListing(SponsoredListingInvoice invoice)
        {
            if (await this.sponsoredListingInvoiceRepository.UpdateAsync(invoice))
            {
                var existingSponsoredListing = await this.sponsoredListingRepository
                                                         .GetByInvoiceIdAsync(invoice.SponsoredListingInvoiceId);

                if (existingSponsoredListing == null && invoice.PaymentStatus == PaymentStatus.Paid)
                {
                    var activeListing = await this.sponsoredListingRepository
                                                  .GetActiveSponsorAsync(invoice.DirectoryEntryId, invoice.SponsorshipType);

                    if (activeListing == null)
                    {
                        // if the invoice is paid and there is no existing sponsored listing, create one
                        var sponsoredListing = await this.sponsoredListingRepository.CreateAsync(
                            new SponsoredListing()
                            {
                                DirectoryEntryId = invoice.DirectoryEntryId,
                                CampaignStartDate = invoice.CampaignStartDate,
                                CampaignEndDate = invoice.CampaignEndDate,
                                SponsoredListingInvoiceId = invoice.SponsoredListingInvoiceId,
                                SponsorshipType = invoice.SponsorshipType,
                                SubCategoryId = invoice.SubCategoryId,
                            });

                        invoice.SponsoredListingId = sponsoredListing.SponsoredListingId;
                        await this.sponsoredListingInvoiceRepository.UpdateAsync(invoice);
                    }
                    else
                    {
                        // extend the existing listing
                        activeListing.CampaignEndDate = invoice.CampaignEndDate;

                        // set the current active listing's invoice to the new invoice
                        activeListing.SponsoredListingInvoiceId = invoice.SponsoredListingInvoiceId;
                        await this.sponsoredListingRepository.UpdateAsync(activeListing);

                        // set the new invoice's sponsored listing to the current active listing
                        invoice.SponsoredListingId = activeListing.SponsoredListingId;
                        await this.sponsoredListingInvoiceRepository.UpdateAsync(invoice);
                    }

                    this.ClearCachedItems();
                }
            }
        }

        private async Task<IEnumerable<DirectoryEntry>> FilterEntries(int? subCategoryId)
        {
            var entries = await this.directoryEntryRepository.GetAllowableAdvertisers();

            if (subCategoryId.HasValue)
            {
                entries = entries.Where(e => e.SubCategoryId == subCategoryId.Value).ToList();
            }

            entries = entries.OrderBy(e => e.Name)
                             .ToList();

            await this.GetSubCateogryOptions();

            return entries;
        }

        private async Task GetSubCateogryOptions()
        {
            this.ViewBag.SubCategories = (await this.subCategoryRepository
                                                    .GetAllActiveSubCategoriesAsync())
                                                    .OrderBy(sc => sc.Category.Name)
                                                    .ThenBy(sc => sc.Name)
                                                    .ToList();
        }

        private bool IsOldEnough(DirectoryEntry directoryEntry)
        {
            if (directoryEntry.CreateDate == DateTime.MinValue)
            {
                return false; // Or throw an exception if CreateDate is required
            }

            if (directoryEntry.DirectoryStatus == DirectoryStatus.Verified)
            {
                return true;
            }

            // Check if the entry is at least 30 days old
            return (DateTime.UtcNow - directoryEntry.CreateDate).TotalDays >= IntegerConstants.UnverifiedMinimumDaysListedBeforeAdvertising;
        }
    }
}