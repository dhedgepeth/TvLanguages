﻿using MailKit;
using System.Net;
using System.Net.Http.Formatting;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Deploy.Infrastructure.Extensions;

namespace TvLanguages.Web;

public class TvMazeUtility
{
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IContentService _contentService;
    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly MediaUrlGeneratorCollection _mediaUrlGeneratorCollection;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IContentTypeBaseServiceProvider _contentTypeBaseServiceProvider;
    private readonly ILogger<TvMazeUtility> _logger;
    private readonly IVariationContextAccessor _variationContextAccessor;

    Dictionary<string, string> Descriptions = new() {
        { "en", "was a great TV Show" },
        { "da", "som et godt tv-program" },
        { "es", "como un gran programa de televisión" },
        { "fr", "était une excellente émission de télévision" },
        { "de", "war eine großartige Fernsehsendung" },
        { "it", "era un ottimo programma televisivo" },
        { "pt", "foi um ótimo programa de TV" },
        { "ru", "было отличным телешоу" },
        { "ja", "素晴らしいテレビ番組でした" },
        { "ko", "좋은 TV 프로그램이었습니다" },
        { "zh", "是一档很棒的电视节目" },
        { "ar", "كانت برنامج تلفزيوني رائع" },
        { "hi", "एक शानदार टीवी शो था" },
        { "tr", "harika bir TV programıydı" },
        { "nl", "was een geweldige tv-show" },
        { "sv", "var en fantastisk TV-show" },
        { "fi", "oli mahtava TV-ohjelma" },
        { "no", "var en flott TV-show" },
        { "pl", "był świetnym programem telewizyjnym" },
        { "hu", "nagyszerű TV-műsor volt" },
        { "cs", "byl skvělý televizní pořad" },
        { "ro", "a fost un program TV minunat" },
        { "el", "ήταν ένα υπέροχο τηλεοπτικό σόου" },
        { "id", "adalah acara TV yang hebat" },
        { "th", "เป็นรายการทีวีที่ยอดเยี่ยม" }
     };

    public TvMazeUtility(IUmbracoContextFactory umbracoContextFactory,
        IContentService contentService,
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        MediaUrlGeneratorCollection mediaUrlGeneratorCollection,
        IShortStringHelper shortStringHelper,
        IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        ILogger<TvMazeUtility> logger,
        IVariationContextAccessor variationContextAccessor)
    {
        _umbracoContextFactory = umbracoContextFactory;
        _contentService = contentService;
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _mediaUrlGeneratorCollection = mediaUrlGeneratorCollection;
        _shortStringHelper = shortStringHelper;
        _contentTypeBaseServiceProvider = contentTypeBaseServiceProvider;
        _logger = logger;
        _variationContextAccessor = variationContextAccessor;
    }

    public string MoveTvShowsFromTvMazeToUmbraco()
    {
        int page = 0;

        Uri ShowsAPI(int page) => new($"https://api.tvmaze.com/shows?page={page}");

        HttpClient client = new();

        while (true)
        {
            var response = client.GetAsync(ShowsAPI(page++)).Result;
            var json = response.Content.ReadAsStringAsync().Result;
            var shows = response.Content.ReadAsAsync<TVMazeShow[]>(new[] { new JsonMediaTypeFormatter() }).Result;
            try { response.EnsureSuccessStatusCode(); } catch { break; }
            if (shows.Any())
            {
                foreach(var show in shows)
                {
                    InsertedOrUpdated(show);
                }
            }
        }
        return $"Sync complete until page {page}";
    }

    private bool InsertedOrUpdated(TVMazeShow show)
    {

        using (var umbracoContextReference = _umbracoContextFactory.EnsureUmbracoContext())
        {
            var TvshowLibrary = umbracoContextReference.UmbracoContext.Content.GetById(_contentService.GetRootContent().First().Id) as TVshowLibrary;
            TVshowPage existingTvShowInUmbraco = null;
            var existingTvShowsInUmbraco = TvshowLibrary.Children<TVshowPage>(_variationContextAccessor).Where(t => t.TvShowID == show.Id.ToString());

            if (existingTvShowsInUmbraco?.Any() ?? false)
            {
                if (existingTvShowsInUmbraco.Count() > 0)
                {
                    existingTvShowInUmbraco = existingTvShowsInUmbraco.OrderBy(t => t.CreateDate).First();
                    foreach (var showToDelete in existingTvShowsInUmbraco.Where(s => s.Id != existingTvShowInUmbraco.Id))
                    {
                        _contentService.Delete(_contentService.GetById(showToDelete.Id));
                    }
                }
                else
                {
                    existingTvShowInUmbraco = existingTvShowsInUmbraco.FirstOrDefault();
                }
            }

            if (existingTvShowInUmbraco == null)
            {
                var media = ImportMediaFromTVMazeToUmbraco(show);
                var newTvShow = _contentService.Create(show.Name, TvshowLibrary.Id, TVshowPage.ModelTypeAlias);
                newTvShow.SetValue(nameof(TVshowPage.TvShowID), show.Id);

                if (media != null)
                {
                    newTvShow.SetValue(nameof(TVshowPage.Thumbnail), media.GetUdi());
                }

                foreach (var description in Descriptions)
                {
                    newTvShow.SetCultureName(show.Name, description.Key);
                    newTvShow.SetValue(nameof(TVshowPage.Description), $"{show.Name} {description.Value}", description.Key);
                }

                _contentService.SaveAndPublish(newTvShow);
                return true;
            }
            return Updated(show, existingTvShowInUmbraco);
        }
    }

    public IMedia ImportMediaFromTVMazeToUmbraco(TVMazeShow tvMazeShow)
    {

        if (tvMazeShow == null || string.IsNullOrEmpty(tvMazeShow.Name) || string.IsNullOrEmpty(tvMazeShow.Image?.Original))
        {
            return null;
        }

        var webRequest = (HttpWebRequest)WebRequest.Create(tvMazeShow.Image.Original);
        webRequest.AllowWriteStreamBuffering = true;
        webRequest.Timeout = 30000;

        var fileName = $"{tvMazeShow.Id}_{GetFileNameFromUrl(tvMazeShow.Image.Original)}";

        var existingFolder = GetMediaFolderFromUmbraco(tvMazeShow.Name);

        var webResponse = webRequest.GetResponse();
        var stream = webResponse.GetResponseStream();

        IMedia media = _mediaService.CreateMedia(fileName, existingFolder.Id, Constants.Conventions.MediaTypes.Image);

        media.SetValue(_mediaFileManager, _mediaUrlGeneratorCollection, _shortStringHelper, _contentTypeBaseServiceProvider, Constants.Conventions.Media.File, fileName, stream);
        try
        {
            _mediaService.Save(media);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, fileName);
            return null;
        }

        return media;
    }

    private IMedia GetMediaFolderFromUmbraco(string tvShowName)
    {
        var parentFolder = _mediaService.GetRootMedia().First();
        if (parentFolder == null)
        {
            throw new FolderNotFoundException($"No Folder exists in the Media Library, please create one.");
        }
        return parentFolder;
    }


    private string GetFileNameFromUrl(string url)
    {
        // Get the last part of the URL after the last slash '/'
        int lastSlashIndex = url.LastIndexOf('/');
        string filenameWithExtension = url.Substring(lastSlashIndex + 1);

        return filenameWithExtension;
    }


    private bool Updated(TVMazeShow show, TVshowPage existingTvShowInUmbraco)
    {
        // todo
        return false;
    }
}