﻿using System.Text;
using System.Xml.Linq;
using DirectoryManager.Data.Enums;
using DirectoryManager.Data.Models;
using DirectoryManager.Web.Services.Interfaces;

namespace DirectoryManager.Web.Services.Implementations
{
    public class RssFeedService : IRssFeedService
    {
        public XDocument GenerateRssFeed(
            IEnumerable<DirectoryEntry> directoryEntries,
            string feedTitle,
            string feedLink,
            string feedDescription)
        {
            var rss = new XElement(
                "rss",
                new XAttribute("version", "2.0"),
                new XElement(
                    "channel",
                    new XElement("title", feedTitle),
                    new XElement("link", feedLink),
                    new XElement("description", feedDescription),
                    directoryEntries.Select(entry =>
                        new XElement(
                            "item",
                            new XElement("title", this.GetFormattedTitle(entry)),
                            new XElement("link", string.IsNullOrEmpty(entry.LinkA) ? entry.Link : entry.LinkA),
                            new XElement("description", this.FormatDescription(entry)),
                            new XElement("pubDate", entry.CreateDate.ToString("R"))))));

            return new XDocument(rss);
        }

        /// <summary>
        /// Formats the RSS item title based on the directory status.
        /// </summary>
        private string GetFormattedTitle(DirectoryEntry entry)
        {
            return entry.DirectoryStatus switch
            {
                DirectoryStatus.Scam => $"&#x274C; {entry.Name}",
                DirectoryStatus.Verified => $"&#x2705; {entry.Name}",
                DirectoryStatus.Questionable => $"&#x2753; {entry.Name}",
                _ => entry.Name
            };
        }

        /// <summary>
        /// Formats the description of the RSS item.
        /// </summary>
        private string FormatDescription(DirectoryEntry entry)
        {
            var descriptionBuilder = new StringBuilder();

            // Retrieve category and subcategory info
            var categoryInfo = entry.SubCategory?.Category != null
                ? $"{entry.SubCategory.Category.Name} > {entry.SubCategory.Name}"
                : entry.SubCategory?.Name ?? "Uncategorized";

            // Append category info and main description (encoded)
            descriptionBuilder.Append(categoryInfo).Append(" : ").Append(entry.Description);

            // Append note if it exists
            if (!string.IsNullOrWhiteSpace(entry.Note))
            {
                descriptionBuilder.Append(" - Note: ").Append(entry.Note);
            }

            // Append additional fields if they are not null or empty
            if (!string.IsNullOrWhiteSpace(entry.Location))
            {
                descriptionBuilder.Append(" - Location: ").Append(entry.Location);
            }

            if (!string.IsNullOrWhiteSpace(entry.Processor))
            {
                descriptionBuilder.Append(" - Processor: ").Append(entry.Processor);
            }

            if (!string.IsNullOrWhiteSpace(entry.Contact))
            {
                descriptionBuilder.Append(" - Contact: ").Append(entry.Contact);
            }

            return descriptionBuilder.ToString();
        }
    }
}