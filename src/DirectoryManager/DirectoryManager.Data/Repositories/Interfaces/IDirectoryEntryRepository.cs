﻿using DirectoryManager.Data.Models;

namespace DirectoryManager.Data.Repositories.Interfaces
{
    public interface IDirectoryEntryRepository
    {
        Task<DirectoryEntry> GetByIdAsync(int id);
        Task<DirectoryEntry> GetByLinkAsync(string link);
        Task<IEnumerable<DirectoryEntry>> GetAllAsync();
        Task<IEnumerable<DirectoryEntry>> GetAllBySubCategoryIdAsync(int subCategoryId);
        Task CreateAsync(DirectoryEntry entry);
        Task UpdateAsync(DirectoryEntry entry);
        Task DeleteAsync(int id);
        public DateTime GetLastRevisionDate();
    }
}