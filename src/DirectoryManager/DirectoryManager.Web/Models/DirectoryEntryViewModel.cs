﻿using DirectoryManager.Data.Models;
using DirectoryManager.Web.Enums;

namespace DirectoryManager.Web.Models
{

    public class DirectoryEntryViewModel
    {
        public DirectoryEntry DirectoryEntry { get; set; }
        public DateDisplayOption DateOption { get; set; } = DateDisplayOption.NotDisplayed;
    }


}