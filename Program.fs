module Agg
open DotNetEnv

Env.Load() |> ignore

open System
open System.IO
open System.Xml.Linq
type RssItemGenerator = unit -> Async<string option>

let rssItemGenerators: RssItemGenerator[] = [|
    MostRecentCommit.getMostRecentPushEventAsRssString
    MostRecentMovieRating.getMostRecentMovieRatingAsRssString
    MostRecentFlickrPhoto.getMostRecentFlickrPhotoAsRssString
|]

let generateRssFeed (items: string list) =
    let channel = 
        XElement(XName.Get("channel"),
            XElement(XName.Get("title"), "Joe's digital journal"),
            XElement(XName.Get("description"), "Sharing my thoughts"),
            XElement(XName.Get("lastBuildDate"), DateTime.Now.ToString("R"))
        )
    
    // Add all valid items to the channel
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
    doc

// Main program
[<EntryPoint>]
let main argv =
    async {
        printfn "Generating RSS feed..."
    //     
    //     Env.Load() |> ignore
    //
    // // Get auth token from environment
    //     let authToken = Environment.GetEnvironmentVariable("API_TOKEN")
    //     
    //     // Check if token exists
    //     if String.IsNullOrEmpty(authToken) then
    //         printfn "Error: API_TOKEN not found in .env file"
    //         1
    //     else
        
        // Execute all RSS item generators in parallel
        let! results = 
            rssItemGenerators
            |> Array.map (fun generator -> generator())
            |> Async.Parallel
        
        // Filter out None results and extract the strings
        let validItems = 
            results
            |> Array.choose id
            |> Array.toList
        
        printfn "Generated %d RSS items" validItems.Length
        
        // Generate the complete RSS feed
        let rssFeed = generateRssFeed validItems
        
        // Write to results.xml in working directory
        let projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName
        let filePath = Path.Combine(projectRoot, "results.xml")
        
        rssFeed.Save(filePath)
        
        printfn $"RSS feed saved to: %s{filePath}"
        return 0
    }
    |> Async.RunSynchronously