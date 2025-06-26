module Agg

open DotNetEnv
open System
open System.IO
open System.Xml.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting

Env.Load() |> ignore

type RssItemGenerator = unit -> Async<string option>

let rssItemGenerators: RssItemGenerator[] = [|
    MostRecentCommit.getMostRecentPushEventAsRssString
    MostRecentMovieRating.getMostRecentMovieRatingAsRssString
    MostRecentMovieRating.getMostRecentEpisodeRatingAsRssString
    MostRecentPhoto.getMostRecentFlickrPhotoAsRssString
|]

let generateRssFeed (items: string list) =
    let channel = 
        XElement(XName.Get("channel"),
            XElement(XName.Get("title"), "Joe's digital journal"),
            XElement(XName.Get("description"), "A curated stream of my discoveries, thoughts, and activities"),
            XElement(XName.Get("lastBuildDate"), DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc))
        )
    
    items
    |> List.filter (fun item -> not (String.IsNullOrWhiteSpace(item)))
    |> List.iter (fun item -> 
        let itemElement = XElement.Parse(item)
        channel.Add(itemElement))
    
    let rss = 
        XElement(XName.Get("rss"),
            XAttribute(XName.Get("version"), "2.0"),
            channel
        )
    
    let declaration = XDeclaration("1.0", "utf-8", "yes")
    let doc = XDocument(rss)
    doc.Declaration <- declaration
    
    // Add XML stylesheet processing instruction for browser syntax highlighting
    let styleSheet = XProcessingInstruction("xml-stylesheet", "type=\"text/xml\"")
    doc.AddFirst(styleSheet)
    
    doc

let generateRssToWwwroot () =
    async {
        printfn "Generating RSS feed..."
        let! results = 
            rssItemGenerators
            |> Array.map (fun generator -> generator())
            |> Async.Parallel
        
        let validItems = 
            results
            |> Array.choose id
            |> Array.toList
        
        printfn $"Generated %d{validItems.Length} RSS items"
        let rssFeed = generateRssFeed validItems
        
        Directory.CreateDirectory("wwwroot") |> ignore
        let filePath = Path.Combine("wwwroot", "journal.xml")
        rssFeed.Save(filePath)
        printfn $"RSS feed saved to: %s{filePath}"
    }

let startScheduler () =
    let timer = new System.Threading.Timer(
        (fun _ -> generateRssToWwwroot() |> Async.RunSynchronously),
        null,
        TimeSpan.Zero,
        TimeSpan.FromHours(12.0))
    timer

[<EntryPoint>]
let main argv =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseUrls("http://0.0.0.0:5001") |> ignore
    let app = builder.Build()

    app.MapGet("/", fun (context: Microsoft.AspNetCore.Http.HttpContext) ->
        let filePath = Path.Combine("wwwroot", "journal.xml")
        if File.Exists(filePath) then
            context.Response.ContentType <- "application/xml; charset=utf-8"
            context.Response.SendFileAsync(filePath)
        else
            context.Response.StatusCode <- 404
            System.Threading.Tasks.Task.CompletedTask
    ) |> ignore
    
    app.UseStaticFiles(StaticFileOptions(FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")))) |> ignore
    
    startScheduler() |> ignore
    generateRssToWwwroot() |> Async.RunSynchronously
    
    app.Run()
    0