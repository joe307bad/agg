module MostRecentPhoto

open System
open System.Net.Http
open System.Text.Json
open System.Xml.Linq

// Simple record for Flickr photo
type FlickrPhoto = {
    Id: string
    Server: string
    Secret: string
    DateUpload: string
    Title: string
    Url: string
    MyPhotos: string
}

let httpClient = new HttpClient()

// Convert FlickrPhoto to RSS item XML
let flickrPhotoToRssItem (photo: FlickrPhoto) =
    let title = if String.IsNullOrEmpty(photo.Title) then "Recent Photo" else photo.Title
    let description = $"My latest photo is titled '%s{title}'"
    let pubDate = System.DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(int64 photo.DateUpload).DateTime, System.DateTimeKind.Utc)
    
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), photo.Url),
        XElement(XName.Get("guid"), photo.Id),
        XElement(XName.Get("pubDate"), pubDate),
        XElement(XName.Get("contentType"),"photo-upload")
    )

// Get the most recent Flickr photo and return as RSS XML
let getMostRecentFlickrPhotoAsRss () = async {
    try
        let flickrApiKey = System.Environment.GetEnvironmentVariable("FLICKR_API_KEY")
        if String.IsNullOrEmpty(flickrApiKey) then
            printfn "FLICKR_API_KEY environment variable not set"
            return None
        else
            let url = sprintf "https://api.flickr.com/services/rest/?method=flickr.people.getPublicPhotos&api_key=%s&user_id=201450104@N05&per_page=1&page=1&format=json&nojsoncallback=1&extras=date_upload" flickrApiKey
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            
            let jsonDoc = JsonDocument.Parse(content)
            let photos = jsonDoc.RootElement.GetProperty("photos")
            let photoArray = photos.GetProperty("photo").EnumerateArray()
            
            match photoArray |> Seq.tryHead with
            | Some photoElement ->
                let id = try photoElement.GetProperty("id").GetString() with _ -> ""
                let server = try photoElement.GetProperty("server").GetString() with _ -> ""
                let secret = try photoElement.GetProperty("secret").GetString() with _ -> ""
                let dateUpload = try photoElement.GetProperty("dateupload").GetString() with _ -> ""
                let title = try photoElement.GetProperty("title").GetString() with _ -> ""
                
                let photoUrl = if not (String.IsNullOrEmpty(id)) then 
                                sprintf "https://live.staticflickr.com/%s/%s_%s_c.jpg" server id secret
                               else ""
                
                let flickrPhoto = {
                    Id = id
                    Server = server
                    Secret = secret
                    DateUpload = dateUpload
                    Title = title
                    Url = photoUrl
                    MyPhotos = "https://www.flickr.com/photos/joe307bad/"
                }
                
                let rssItem = flickrPhotoToRssItem flickrPhoto
                return Some rssItem
            | None -> 
                printfn "No photos found"
                return None
            
    with
    | ex -> 
        printfn "Error: %s" ex.Message
        return None
}

// Get RSS XML as string
let getMostRecentFlickrPhotoAsRssString () = async {
    let! rssItem = getMostRecentFlickrPhotoAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}