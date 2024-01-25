using Hangfire;
using TvLanguages.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var umbracoBuilder = builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
.AddDeliveryApi()
.AddComposers();

if (builder.Environment.IsProduction())
{
    RecurringJob.AddOrUpdate<TvMazeUtility>("MoveOneTvShowFromTvMazeToUmbraco", x => x.MoveTvShowsFromTvMazeToUmbraco(), Cron.Daily);

}

umbracoBuilder.Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseHttpsRedirection();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseInstallerEndpoints();
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
