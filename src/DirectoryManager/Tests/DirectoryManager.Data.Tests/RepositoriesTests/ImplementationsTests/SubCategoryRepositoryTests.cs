﻿using DirectoryManager.Data.DbContextInfo;
using DirectoryManager.Data.Models;
using DirectoryManager.Data.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DirectoryManager.Data.Tests.RepositoriesTests.ImplementationsTests
{
    public class SubCategoryRepositoryTests
    {
        private readonly Mock<IApplicationDbContext> mockContext;
        private readonly DbSet<Subcategory> mockDbSet;
        private readonly SubcategoryRepository repository;

        public SubCategoryRepositoryTests()
        {
            this.mockContext = new Mock<IApplicationDbContext>();

            // Sample data
            var data = new List<Subcategory>
            {
                new Subcategory { SubCategoryId = 1, Name = "SubCategory1" },
                new Subcategory { SubCategoryId = 2, Name = "SubCategory2" }
            }.AsQueryable();

            this.mockDbSet = MockHelpers.MockHelpers.GetQueryableMockDbSet(data).Object;
            this.mockContext.Setup(c => c.SubCategories).Returns(this.mockDbSet);

            this.repository = new SubcategoryRepository(this.mockContext.Object);
        }

        [Fact]
        public async Task GetByIdAsync_InvalidId_ReturnsNull()
        {
            var result = await this.repository.GetByIdAsync(999); // Nonexistent Id
            Assert.Null(result);
        }
    }
}
