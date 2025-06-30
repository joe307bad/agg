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

let createHttpClient () =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromSeconds(10.0) // Reduce timeout for faster feedback
    client.DefaultRequestHeaders.Add("User-Agent", "FlickrPhotoFetcher/1.0")
    client.DefaultRequestHeaders.Add("Accept", "application/json")
    client.DefaultRequestHeaders.Add("Connection", "close") // Don't keep connection alive
    client

let httpClient = createHttpClient()

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
        printfn "[%s] TIMEOUT ERROR: Request timed out after 10 seconds" timestamp
    | UnknownError message ->
        printfn "[%s] UNKNOWN ERROR: %s" timestamp message

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

// Enhanced photo data validation
let validatePhotoData (photoElement: JsonElement) =
    let requiredFields = ["id"; "server"; "secret"; "dateupload"]
    let missingFields = 
        requiredFields 
        |> List.filter (fun field -> 
            not (photoElement.TryGetProperty(field).Item1) || 
            String.IsNullOrEmpty(photoElement.GetProperty(field).GetString()))
    
    if missingFields.Length > 0 then
        Error (InvalidPhotoData (String.Join(", ", missingFields)))
    else
        Ok ()

// Simple test function to diagnose connectivity issues
let testFlickrConnection () = async {
    try
        let flickrApiKey = System.Environment.GetEnvironmentVariable("FLICKR_API_KEY")
        if String.IsNullOrEmpty(flickrApiKey) then
            printfn "No API key found"
            return false |> ignore
        
        printfn "[%s] Testing basic connectivity to Flickr..." (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
        
        // Test with a simpler endpoint first
        let testUrl = $"https://api.flickr.com/services/rest/?method=flickr.test.echo&api_key=%s{flickrApiKey}&format=json&nojsoncallback=1"
        
        use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
        use testClient = new HttpClient()
        testClient.Timeout <- TimeSpan.FromSeconds(5.0)
        
        let! response = testClient.GetAsync(testUrl, cts.Token) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        
        printfn "[%s] Test response: %s" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")) content
        return response.IsSuccessStatusCode
        
    with
    | ex ->
        printfn "[%s] Connection test failed: %s" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")) ex.Message
        return false
}

// Get the most recent Flickr photo and return as RSS XML
let getMostRecentFlickrPhotoAsRss () = async {
    try
        // Check API key
        let flickrApiKey = System.Environment.GetEnvironmentVariable("FLICKR_API_KEY")
        if String.IsNullOrEmpty(flickrApiKey) then
            logError ApiKeyMissing
            return None
        else
            // Make API request with detailed error handling
            let url = $"https://api.flickr.com/services/rest/?method=flickr.people.getPublicPhotos&api_key=%s{flickrApiKey}&user_id=201450104@N05&per_page=1&page=1&format=json&nojsoncallback=1&extras=date_upload"
            
            printfn "[%s] INFO: Making request to Flickr API..." (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            printfn "[DEBUG] Request URL: %s" (url.Replace(flickrApiKey, "***API_KEY***"))
            
            try
                // Use a cancellation token for better control
                use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10.0))
                
                let! response = httpClient.GetAsync(url, cts.Token) |> Async.AwaitTask
                
                printfn "[%s] INFO: Received response with status: %A" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")) response.StatusCode
                
                if not response.IsSuccessStatusCode then
                    let statusCode = int response.StatusCode
                    let reason = response.ReasonPhrase
                    logError (NetworkError ($"HTTP {statusCode}: {reason}", Some statusCode))
                    return None |> ignore 
                
                printfn "[%s] INFO: Reading response content..." (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                printfn "[%s] INFO: Response length: %d characters" (DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")) content.Length
                
                // Parse JSON with error handling
                try
                    let jsonDoc = JsonDocument.Parse(content)
                    let root = jsonDoc.RootElement
                    
                    // Check if API returned an error
                    if root.TryGetProperty("stat").Item1 then
                        let stat = root.GetProperty("stat").GetString()
                        if stat = "fail" then
                            let errorMsg = 
                                if root.TryGetProperty("message").Item1 then
                                    root.GetProperty("message").GetString()
                                else "Unknown API error"
                            logError (ApiResponseError errorMsg)
                            return None |> ignore 
                    
                    // Extract photos
                    if not (root.TryGetProperty("photos").Item1) then
                        logError (JsonParseError "Missing 'photos' property in API response")
                        return None |> ignore 
                    
                    let photos = root.GetProperty("photos")
                    
                    if not (photos.TryGetProperty("photo").Item1) then
                        logError (JsonParseError "Missing 'photo' array in API response")
                        return None |> ignore 
                    
                    let photoArray = photos.GetProperty("photo").EnumerateArray()
                    
                    match photoArray |> Seq.tryHead with
                    | Some photoElement ->
                        // Validate photo data
                        match validatePhotoData photoElement with
                        | Error error ->
                            logError error
                            return None
                        | Ok () ->
                            // Extract photo data with fallbacks
                            let id = photoElement.GetProperty("id").GetString()
                            let server = photoElement.GetProperty("server").GetString()
                            let secret = photoElement.GetProperty("secret").GetString()
                            let dateUpload = photoElement.GetProperty("dateupload").GetString()
                            let title = 
                                if photoElement.TryGetProperty("title").Item1 then
                                    photoElement.GetProperty("title").GetString()
                                else ""
                            
                            let photoUrl = $"https://live.staticflickr.com/%s{server}/%s{id}_%s{secret}_c.jpg"
                            
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
                    | None -> 
                        logError NoPhotosFound
                        return None
                        
                with 
                | :? JsonException as jsonEx ->
                    logError (JsonParseError $"Invalid JSON structure: {jsonEx.Message}")
                    return None
                    
            with
            | :? TaskCanceledException as tcEx ->
                // Check if it's actually a timeout vs user cancellation
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