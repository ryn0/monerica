﻿using DirectoryManager.Data.DbContextInfo;
using DirectoryManager.Data.Extensions;
using DirectoryManager.Data.Repositories.Interfaces;
using DirectoryManager.Services.Implementations;
using DirectoryManager.Services.Interfaces;
using DirectoryManager.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Build configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile(DirectoryManager.Common.Constants.StringConstants.AppSettingsFileName, optional: true, reloadOnChange: true)
    .Build();

// Get the default expiration time span from configuration
int hours = config.GetValue<int?>("ExpirationHours") ?? DirectoryManager.SponsoredListingReminder.Constants.IntegerConstants.DefaultHours;
var expirationTimeSpan = TimeSpan.FromHours(hours);

// Email message key and renewal link template from configuration
var emailMessageKey = config.GetValue<string>("EmailKeys:SponsoredListingExpirationReminder") ??
    DirectoryManager.SponsoredListingReminder.Constants.StringConstants.SponsoredListingExpirationReminder;
var renewalLinkTemplate = config.GetValue<string>("RenewalLinkTemplate")
    ?? throw new Exception("RenewalLinkTemplate is missing in configuration.");

// Register services in the service container
var serviceProvider = new ServiceCollection()
    .AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(config.GetConnectionString(DirectoryManager.Data.Constants.StringConstants.DefaultConnection)))
    .AddRepositories() // Add repositories using the extension method
    .AddSingleton<IEmailService, EmailService>(provider =>
    {
        var emailConfig = new SendGridConfig
        {
            ApiKey = config["SendGrid:ApiKey"] ?? throw new InvalidOperationException("SendGrid:ApiKey is missing in configuration."),
            SenderEmail = config["SendGrid:SenderEmail"] ?? throw new InvalidOperationException("SendGrid:SenderEmail is missing in configuration."),
            SenderName = config["SendGrid:SenderName"] ?? "Default Sender Name" // Default value if SenderName is not provided.
        };

        return new EmailService(emailConfig);
    })
    .BuildServiceProvider();

// Retrieve required services
var listingRepo = serviceProvider.GetRequiredService<ISponsoredListingRepository>();
var invoiceRepo = serviceProvider.GetRequiredService<ISponsoredListingInvoiceRepository>();
var emailMessageRepo = serviceProvider.GetRequiredService<IEmailMessageRepository>();
var emailService = serviceProvider.GetRequiredService<IEmailService>();

// Fetch the email message template
var emailMessage = emailMessageRepo.GetByKey(emailMessageKey);

if (emailMessage == null)
{
    Console.WriteLine($"Email template with key '{emailMessageKey}' not found. Exiting.");
    return;
}

// Fetch expiring listings
var expiringListings = await listingRepo.GetExpiringListingsWithinAsync(expirationTimeSpan);

Console.WriteLine($"Found {expiringListings.Count()} listings expiring within {hours} hours.");

// Process each listing
foreach (var listing in expiringListings)
{
    var invoice = await invoiceRepo.GetByIdAsync(listing.SponsoredListingInvoiceId);

    if (invoice == null)
    {
        Console.WriteLine($"No invoice found for Listing ID: {listing.SponsoredListingId}");
        continue;
    }

    if (string.IsNullOrWhiteSpace(invoice.Email))
    {
        Console.WriteLine($"No email associated with Invoice ID: {invoice.SponsoredListingInvoiceId}");
        continue;
    }

    if (invoice.IsReminderSent)
    {
        Console.WriteLine($"Reminder already sent for Invoice ID: {invoice.SponsoredListingInvoiceId}");
        continue;
    }

    // Generate the renewal link using the template and replace placeholders
    var renewalLink = renewalLinkTemplate
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.DirectoryEntryIdPlaceholder, listing.DirectoryEntryId.ToString())
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.SponsorshipTypePlaceholder, listing.SponsorshipType.ToString());

    // Prepare the email content by replacing placeholders
    var plainTextContent = emailMessage.EmailBodyText
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.ListingNameToken, listing.DirectoryEntry?.Name ??
            DirectoryManager.SponsoredListingReminder.Constants.StringConstants.DefaultListingName)
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.ListingRenewalLinkToken, renewalLink);

    var htmlContent = emailMessage.EmailBodyHtml
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.ListingNameToken, listing.DirectoryEntry?.Name ??
            DirectoryManager.SponsoredListingReminder.Constants.StringConstants.DefaultListingName)
        .Replace(DirectoryManager.SponsoredListingReminder.Constants.StringConstants.ListingRenewalLinkToken, renewalLink);

    // Send the email using the EmailService
    await emailService.SendEmailAsync(emailMessage.EmailSubject, plainTextContent, htmlContent, new List<string> { invoice.Email });

    // Mark the reminder as sent
    invoice.IsReminderSent = true;
    var updated = await invoiceRepo.UpdateAsync(invoice);

    if (updated)
    {
        Console.WriteLine($"Reminder sent and marked for Invoice ID: {invoice.SponsoredListingInvoiceId}");
    }
    else
    {
        Console.WriteLine($"Failed to update Invoice ID: {invoice.SponsoredListingInvoiceId}");
    }
}

Console.WriteLine("Processing complete.");