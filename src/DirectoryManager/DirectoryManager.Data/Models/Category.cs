﻿using System.ComponentModel.DataAnnotations;
using DirectoryManager.Data.Models.BaseModels;

namespace DirectoryManager.Data.Models
{
    public class Category : UserStateInfo
    {
        [Key]
        public int CategoryId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string CategoryKey { get; set; } = string.Empty;

        [MaxLength(160)]
        public string? MetaDescription { get; set; }

        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(2000)]
        public string? Note { get; set; }
    }
}
