module MostRecentPhoto

open System
open System.Globalization
open System.Net.Http
open System.Xml.Linq

type FlickrPhoto = {
    Id: string
    Title: string
    Description: string
    Url: string
    PubDate: string
}

let convertRssDateToIso (rssDate: string) : string =
    try
        // Parse the RSS date format: "Mon, 30 Jun 2025 17:50:16 -0700"
        let parsedDate = DateTime.ParseExact(rssDate, "ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)
        
        // Convert to UTC and format as ISO 8601
        parsedDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    with
    | _ -> 
        // Fallback: try without timezone
        try
            let dateWithoutTz = rssDate.Substring(0, rssDate.LastIndexOf(' '))
            let parsedDate = DateTime.ParseExact(dateWithoutTz, "ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            parsedDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
        with
        | _ -> DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") 

let private parseFlickrRss (rssContent: string) : FlickrPhoto option =
    try
        let doc = XDocument.Parse(rssContent)
        let ns = XNamespace.Get("http://www.w3.org/2005/Atom")
        
        // Try RSS format first
        let item = doc.Descendants(XName.Get("item")) |> Seq.tryHead
        
        match item with
        | Some item ->
            let title = 
                match item.Element(XName.Get("title")) with
                | null -> ""
                | elem -> elem.Value
            let description = 
                match item.Element(XName.Get("title")) with
                | null -> ""
                | elem -> $"This photo is titled '%s{elem.Value}'"
            let link = 
                match item.Element(XName.Get("link")) with
                | null -> ""
                | elem -> elem.Value
            let guid = 
                match item.Element(XName.Get("guid")) with
                | null -> ""
                | elem -> elem.Value
            let pubDate = 
                match item.Element(XName.Get("pubDate")) with
                | null -> ""
                | elem -> convertRssDateToIso elem.Value
            
            Some {
                Id = guid
                Title = title
                Description = description
                Url = link
                PubDate = pubDate
            }
        | None ->
            // Try Atom format as fallback
            let entry = doc.Descendants(ns + "entry") |> Seq.tryHead
            match entry with
            | Some entry ->
                let title = 
                    match entry.Element(ns + "title") with
                    | null -> ""
                    | elem -> elem.Value
                let summary = 
                    match entry.Element(ns + "summary") with
                    | null -> ""
                    | elem -> elem.Value
                let link = 
                    entry.Elements(ns + "link") 
                    |> Seq.tryFind (fun l -> 
                        match l.Attribute(XName.Get("rel")) with
                        | null -> false
                        | attr -> attr.Value = "alternate")
                    |> Option.bind (fun l -> 
                        match l.Attribute(XName.Get("href")) with
                        | null -> Some ""
                        | attr -> Some attr.Value)
                    |> Option.defaultValue ""
                let id = 
                    match entry.Element(ns + "id") with
                    | null -> ""
                    | elem -> elem.Value
                let updated = 
                    match entry.Element(ns + "updated") with
                    | null -> ""
                    | elem -> elem.Value
                
                Some {
                    Id = id
                    Title = title
                    Description = summary
                    Url = link
                    PubDate = updated
                }
            | None -> None
    with
    | _ -> None

let private createRssItem (photo: FlickrPhoto) : XElement =
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), photo.Title),
        XElement(XName.Get("description"), photo.Description),
        XElement(XName.Get("link"), photo.Url),
        XElement(XName.Get("guid"), photo.Id),
        XElement(XName.Get("pubDate"), photo.PubDate),
        XElement(XName.Get("contentType"), "photo-upload")
    )

let getMostRecentFlickrPhotoAsRssString () : Async<string option> =
    async {
        use client = new HttpClient()
        let url = "https://www.flickr.com/services/feeds/photos_public.gne?id=201450104@N05&format=rss_200"
        
        try
            printfn "Making HTTP request to: %s" url
            let! httpResponse = client.GetAsync(url) |> Async.AwaitTask
            printfn "HTTP Response Status: %A" httpResponse.StatusCode
            printfn "HTTP Response Status Message: %s" httpResponse.ReasonPhrase
            
            if httpResponse.IsSuccessStatusCode then
                let! response = httpResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
                printfn "Response received, length: %d characters" response.Length
                
                match parseFlickrRss response with
                | Some photo ->
                    printfn "Successfully parsed photo: %s" photo.Title
                    let rssItem = createRssItem photo
                    return Some (rssItem.ToString())
                | None ->
                    printfn "Failed to parse RSS response"
                    return None
            else
                printfn "HTTP request failed with status: %A" httpResponse.StatusCode
                return None
        with
        | ex -> 
            printfn "Exception occurred: %s" ex.Message
            return None
    }