module MostRecentPhoto

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
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
    client.Timeout <- TimeSpan.FromSeconds(30.0)
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

// Safe JSON property accessor - mimics JavaScript's optional chaining
let tryGetProperty (propertyName: string) (element: JsonElement) =
    match element.TryGetProperty(propertyName) with
    | (true, prop) -> Some prop
    | (false, _) -> None

let tryGetString (element: JsonElement) =
    try
        Some (element.GetString())
    with
    | _ -> None

// Get the most recent Flickr photo and return as RSS XML
let getMostRecentFlickrPhotoAsRss () = async {
    try
        let flickrApiKey = System.Environment.GetEnvironmentVariable("FLICKR_API_KEY")
        if String.IsNullOrEmpty(flickrApiKey) then
            logError ApiKeyMissing
            return None
        else
            let url = $"https://api.flickr.com/services/rest/?method=flickr.people.getPublicPhotos&api_key=%s{flickrApiKey}&user_id=201450104@N05&per_page=1&page=1&format=json&nojsoncallback=1&extras=date_upload"
            
            try
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                
                if not response.IsSuccessStatusCode then
                    let statusCode = int response.StatusCode
                    let reason = response.ReasonPhrase
                    logError (NetworkError ($"HTTP {statusCode}: {reason}", Some statusCode))
                    return None |> ignore
                
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                printfn "[DEBUG] API Response: %s" (content.Substring(0, min 200 content.Length))
                
                let jsonDoc = JsonDocument.Parse(content)
                let root = jsonDoc.RootElement
                
                // Check API status like JavaScript does
                match tryGetProperty "stat" root with
                | Some stat when stat.GetString() = "fail" ->
                    let errorMsg = 
                        match tryGetProperty "message" root with
                        | Some msg -> msg.GetString()
                        | None -> "Unknown API error"
                    logError (ApiResponseError errorMsg)
                    return None |> ignore
                | _ -> ()
                
                // Navigate the JSON structure like JavaScript: photos?.photos?.photo?.[0]
                match tryGetProperty "photos" root with
                | None -> 
                    logError (JsonParseError "Missing 'photos' property in API response")
                    return None
                | Some photos ->
                    match tryGetProperty "photo" photos with
                    | None ->
                        logError (JsonParseError "Missing 'photo' array in API response")
                        return None
                    | Some photoArray ->
                        let photoElements = photoArray.EnumerateArray() |> Seq.toList
                        if photoElements.IsEmpty then
                            logError NoPhotosFound
                            return None
                        else
                            let photoElement = photoElements.[0]
                            
                            // Extract data with safe access like JavaScript
                            let id = tryGetProperty "id" photoElement |> Option.bind tryGetString |> Option.defaultValue ""
                            let server = tryGetProperty "server" photoElement |> Option.bind tryGetString |> Option.defaultValue ""
                            let secret = tryGetProperty "secret" photoElement |> Option.bind tryGetString |> Option.defaultValue ""
                            let dateUpload = tryGetProperty "dateupload" photoElement |> Option.bind tryGetString |> Option.defaultValue ""
                            let title = tryGetProperty "title" photoElement |> Option.bind tryGetString |> Option.defaultValue ""
                            
                            printfn "[DEBUG] Extracted data: id=%s, server=%s, secret=%s, date=%s, title=%s" id server secret dateUpload title
                            
                            // Build photo URL like JavaScript (without _c suffix to match JS)
                            let photoUrl = 
                                if not (String.IsNullOrEmpty(id)) && not (String.IsNullOrEmpty(server)) && not (String.IsNullOrEmpty(secret)) then 
                                    $"https://live.staticflickr.com/%s{server}/%s{id}_%s{secret}.jpg"
                                else 
                                    ""
                            
                            if String.IsNullOrEmpty(photoUrl) then
                                logError (InvalidPhotoData "id, server, or secret")
                                return None
                            else
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
                                printfn "[%s] SUCCESS: Retrieved photo with ID %s" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")) id
                                return Some rssItem
                                
            with
            | :? TaskCanceledException as tcEx ->
                if tcEx.CancellationToken.IsCancellationRequested then
                    logError TimeoutError
                else
                    logError (UnknownError $"Task was canceled: {tcEx.Message}")
                return None
            | :? HttpRequestException as httpEx ->
                logError (NetworkError (httpEx.Message, None))
                return None
            | :? JsonException as jsonEx ->
                logError (JsonParseError $"Failed to parse JSON response: {jsonEx.Message}")
                return None
            | ex ->
                logError (UnknownError $"Request/parsing error: {ex.Message}")
                return None
            
    with
    | ex -> 
        logError (UnknownError $"Unexpected error in getMostRecentFlickrPhotoAsRss: {ex.Message}")
        printfn "Stack trace: %s" ex.StackTrace
        return None
}

// Get RSS XML as string with enhanced error handling
let getMostRecentFlickrPhotoAsRssString () = async {
    try
        let! rssItem = getMostRecentFlickrPhotoAsRss()
        match rssItem with
        | Some item -> 
            try
                return Some (item.ToString())
            with
            | ex ->
                logError (UnknownError $"XML serialization error: {ex.Message}")
                return None
        | None -> 
            printfn "[%s] INFO: No RSS item to convert to string" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            return None
    with
    | ex ->
        logError (UnknownError $"Error in getMostRecentFlickrPhotoAsRssString: {ex.Message}")
        return None
}