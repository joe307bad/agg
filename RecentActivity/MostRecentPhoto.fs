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

let createHttpClient () =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromSeconds(30.0) // Set explicit timeout
    client.DefaultRequestHeaders.Add("User-Agent", "FlickrPhotoFetcher/1.0")
    client

let httpClient = createHttpClient()

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

// Enhanced error types for better error handling
type FlickrError =
    | ApiKeyMissing
    | NetworkError of string * int option
    | JsonParseError of string
    | ApiResponseError of string
    | NoPhotosFound
    | InvalidPhotoData of string
    | TimeoutError
    | UnknownError of string
// Enhanced logging function
let logError (error: FlickrError) =
    let timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
    match error with
    | ApiKeyMissing -> 
        printfn "[%s] ERROR: FLICKR_API_KEY environment variable is not set or empty" timestamp
    | NetworkError (message, statusCode) ->
        match statusCode with
        | Some code -> printfn "[%s] NETWORK ERROR [%d]: %s" timestamp code message
        | None -> printfn "[%s] NETWORK ERROR: %s" timestamp message
    | JsonParseError message ->
        printfn "[%s] JSON PARSE ERROR: Failed to parse API response - %s" timestamp message
    | ApiResponseError message ->
        printfn "[%s] API ERROR: Flickr API returned an error - %s" timestamp message
    | NoPhotosFound ->
        printfn "[%s] WARNING: No photos found in API response" timestamp
    | InvalidPhotoData field ->
        printfn "[%s] DATA ERROR: Invalid or missing photo data for field: %s" timestamp field
    | TimeoutError ->
        printfn "[%s] TIMEOUT ERROR: Request timed out after 30 seconds" timestamp
    | UnknownError message ->
        printfn "[%s] UNKNOWN ERROR: %s" timestamp message

// Get the most recent Flickr photo and return as RSS XML
let getMostRecentFlickrPhotoAsRss () = async {
    try
        let flickrApiKey = System.Environment.GetEnvironmentVariable("FLICKR_API_KEY")
        if String.IsNullOrEmpty(flickrApiKey) then
            printfn "FLICKR_API_KEY environment variable not set"
            return None
        else
            let url = $"https://api.flickr.com/services/rest/?method=flickr.people.getPublicPhotos&api_key=%s{flickrApiKey}&user_id=201450104@N05&per_page=1&page=1&format=json&nojsoncallback=1&extras=date_upload"
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
                                $"https://live.staticflickr.com/%s{server}/%s{id}_%s{secret}_c.jpg"
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
    | :? JsonException as jsonEx ->
        logError (JsonParseError jsonEx.Message)
        return None
    | ex ->
        logError (UnknownError $"JSON processing error: {ex.Message}")
        return None
}

// Get RSS XML as string
let getMostRecentFlickrPhotoAsRssString () = async {
    let! rssItem = getMostRecentFlickrPhotoAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}