﻿using DirectoryManager.Data.Models;
using DirectoryManager.Data.Repositories.Interfaces;
using DirectoryManager.Utilities.Validation;
using DirectoryManager.Web.Models.ContentSnippet;
using DirectoryManager.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Caching.Memory;

namespace DirectoryManager.Web.Controllers
{
    [Authorize]
    public class ContentSnippetManagementController : BaseController
    {
        private readonly IContentSnippetRepository contentSnippetRepository;
        private readonly ICacheService contentSnippetHelper;

        public ContentSnippetManagementController(
            IContentSnippetRepository contentSnippetRepository,
            ICacheService contentSnippetHelper,
            ITrafficLogRepository trafficLogRepository,
            IUserAgentCacheService userAgentCacheService,
            IMemoryCache cache)
            : base(trafficLogRepository, userAgentCacheService, cache)
        {
            this.contentSnippetRepository = contentSnippetRepository;
            this.contentSnippetHelper = contentSnippetHelper;
        }

        [Route("contentsnippetmanagement")]
        public IActionResult Index()
        {
            var allSnippets = this.contentSnippetRepository.GetAll().OrderBy(x => x.SnippetType.ToString());
            var model = new ContentSnippetEditListModel();

            foreach (var snippet in allSnippets)
            {
                model.Items.Add(new ContentSnippetEditModel()
                {
                    Content = snippet.Content,
                    ContentSnippetId = snippet.ContentSnippetId,
                    SnippetType = snippet.SnippetType
                });
            }

            return this.View(model);
        }

        [Route("contentsnippetmanagement/create")]
        [HttpGet]
        public IActionResult Create()
        {
            var existingSnippets = this.contentSnippetRepository.GetAll().OrderBy(x => x.SnippetType.ToString());

            var model = new ContentSnippetEditModel()
            {
                SnippetType = Data.Enums.SiteConfigSetting.Unknown
            };

            foreach (string snippetType in Enum.GetNames(typeof(Data.Enums.SiteConfigSetting)))
            {
                if (existingSnippets.FirstOrDefault(x => x.SnippetType.ToString() == snippetType) != null)
                {
                    continue;
                }

                model.UnusedSnippetTypes.Add(new SelectListItem()
                {
                    Text = snippetType.ToString(),
                    Value = snippetType.ToString(),
                });
            }

            model.UnusedSnippetTypes = model.UnusedSnippetTypes.OrderBy(x => x.Text).ToList();

            return this.View(model);
        }

        [Route("contentsnippetmanagement/create")]
        [HttpPost]
        public IActionResult Create(ContentSnippetEditModel model)
        {
            if (!this.ModelState.IsValid)
            {
                return this.View(model);
            }

            ValidateInput(model);

            var dbModel = this.contentSnippetRepository.Get(model.ContentSnippetId);

            if (dbModel != null)
            {
                throw new Exception("type already exists");
            }

            this.contentSnippetRepository.Create(new ContentSnippet()
            {
                Content = this.CleanContentInput(model),
                ContentSnippetId = model.ContentSnippetId,
                SnippetType = model.SnippetType
            });

            return this.RedirectToAction("index");
        }

        [Route("contentsnippetmanagement/edit")]
        [HttpGet]
        public IActionResult Edit(int contentSnippetId)
        {
            var dbModel = this.contentSnippetRepository.Get(contentSnippetId);

            if (dbModel == null)
            {
                throw new Exception("Content Snippet does not exist");
            }

            var model = new ContentSnippetEditModel()
            {
                Content = dbModel.Content,
                ContentSnippetId = dbModel.ContentSnippetId,
                SnippetType = dbModel.SnippetType,
            };

            return this.View(model);
        }

        [Route("contentsnippetmanagement/edit")]
        [HttpPost]
        public IActionResult Edit(ContentSnippetEditModel model)
        {
            if (!this.ModelState.IsValid)
            {
                return this.View(model);
            }

            ValidateInput(model);

            var dbModel = this.contentSnippetRepository.Get(model.ContentSnippetId);

            if (dbModel == null)
            {
                throw new Exception("Content Snippet does not exist");
            }

            dbModel.Content = this.CleanContentInput(model);
            dbModel.SnippetType = model.SnippetType;

            this.contentSnippetRepository.Update(dbModel);

            this.contentSnippetHelper.ClearSnippetCache(model.SnippetType);

            this.ClearCachedItems();

            return this.RedirectToAction("index");
        }

        [Route("contentsnippetmanagement/delete")]
        [HttpPost]
        public IActionResult Delete(int contentSnippetId)
        {
            this.contentSnippetRepository.Delete(contentSnippetId);

            return this.RedirectToAction("index");
        }

        private static void ValidateInput(ContentSnippetEditModel model)
        {
            if (model.SnippetType == Data.Enums.SiteConfigSetting.CssHeader &&
                model.Content != null &&
                !CssValidator.IsCssValid(model.Content))
            {
                throw new Exception("Invalid CSS");
            }
        }

        private string? CleanContentInput(ContentSnippetEditModel model)
        {
            var content = string.Empty;

            model.Content ??= content;

            if (model.SnippetType == Data.Enums.SiteConfigSetting.HomePageMetaTags)
            {
                content = model.Content;
            }
            else
            {
                content = model.Content.Trim();
            }

            return content;
        }
    }
}