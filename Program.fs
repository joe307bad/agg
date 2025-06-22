open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Text
open System.Xml
open DotNetEnv

// Simple record to hold our results
type ApiResult = {
    Url: string
    Method: string
    StatusCode: int
    ContentLength: int
    ResponseTime: int64
}

let httpClient = new HttpClient()

// Make a GET request
let makeGetRequest (url: string) = async {
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()
    try
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        stopwatch.Stop()
        
        return {
            Url = url
            Method = "GET"
            StatusCode = int response.StatusCode
            ContentLength = content.Length
            ResponseTime = stopwatch.ElapsedMilliseconds
        }
    with
    | ex -> 
        stopwatch.Stop()
        return {
            Url = url
            Method = "GET"
            StatusCode = 0
            ContentLength = 0
            ResponseTime = stopwatch.ElapsedMilliseconds
        }
}

// Make a POST request with auth header
let makePostRequest (url: string) (authToken: string) (jsonBody: string) = async {
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()
    try
        let content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        let request = new HttpRequestMessage(HttpMethod.Post, url)
        request.Content <- content
        request.Headers.Add("Authorization", sprintf "Bearer %s" authToken)
        
        let! response = httpClient.SendAsync(request) |> Async.AwaitTask
        let! responseContent = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        stopwatch.Stop()
        
        return {
            Url = url
            Method = "POST"
            StatusCode = int response.StatusCode
            ContentLength = responseContent.Length
            ResponseTime = stopwatch.ElapsedMilliseconds
        }
    with
    | ex -> 
        stopwatch.Stop()
        return {
            Url = url
            Method = "POST"
            StatusCode = 0
            ContentLength = 0
            ResponseTime = stopwatch.ElapsedMilliseconds
        }
}

// Generate RSS XML from results
let generateRssXml (results: ApiResult[]) =
    let doc = new XmlDocument()
    
    // Create XML declaration
    let declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null)
    doc.AppendChild(declaration) |> ignore
    
    // Create RSS root element
    let rss = doc.CreateElement("rss")
    rss.SetAttribute("version", "2.0")
    doc.AppendChild(rss) |> ignore
    
    // Create channel element
    let channel = doc.CreateElement("channel")
    rss.AppendChild(channel) |> ignore
    
    // Add channel metadata
    let title = doc.CreateElement("title")
    title.InnerText <- "HTTP Request Results"
    channel.AppendChild(title) |> ignore
    
    let description = doc.CreateElement("description")
    description.InnerText <- "Results from parallel HTTP requests"
    channel.AppendChild(description) |> ignore
    
    let link = doc.CreateElement("link")
    link.InnerText <- "https://example.com"
    channel.AppendChild(link) |> ignore
    
    let pubDate = doc.CreateElement("pubDate")
    pubDate.InnerText <- DateTime.Now.ToString("R")
    channel.AppendChild(pubDate) |> ignore
    
    // Add items for each result
    results |> Array.iter (fun result ->
        let item = doc.CreateElement("item")
        
        let itemTitle = doc.CreateElement("title")
        itemTitle.InnerText <- sprintf "%s %s" result.Method result.Url
        item.AppendChild(itemTitle) |> ignore
        
        let itemDescription = doc.CreateElement("description")
        let statusText = if result.StatusCode = 0 then "Failed" else string result.StatusCode
        itemDescription.InnerText <- sprintf "Status: %s | Size: %d bytes | Response Time: %dms" 
                                               statusText result.ContentLength result.ResponseTime
        item.AppendChild(itemDescription) |> ignore
        
        let itemLink = doc.CreateElement("link")
        itemLink.InnerText <- result.Url
        item.AppendChild(itemLink) |> ignore
        
        let itemGuid = doc.CreateElement("guid")
        itemGuid.InnerText <- sprintf "%s-%s-%d" result.Method result.Url (DateTime.Now.Ticks)
        item.AppendChild(itemGuid) |> ignore
        
        let itemPubDate = doc.CreateElement("pubDate")
        itemPubDate.InnerText <- DateTime.Now.ToString("R")
        item.AppendChild(itemPubDate) |> ignore
        
        channel.AppendChild(item) |> ignore
    )
    
    doc

[<EntryPoint>]
let main argv =
    // Load environment variables from .env file
    Env.Load() |> ignore
    
    // Get auth token from environment
    let authToken = Environment.GetEnvironmentVariable("API_TOKEN")
    
    // Check if token exists
    if String.IsNullOrEmpty(authToken) then
        printfn "Error: API_TOKEN not found in .env file"
        1
    else
        // JSON body for POST request
        let postData = """{"title": "F# Test Post", "body": "Testing from F#", "userId": 1}"""
        
        printfn "Making parallel HTTP requests..."
        
        // Create list of async operations (3 GETs + 1 POST)
        let requests = [
            makeGetRequest "https://httpbin.org/json"
            makeGetRequest "https://jsonplaceholder.typicode.com/posts/1"
            makeGetRequest "https://api.github.com/users/octocat"
            makePostRequest "https://jsonplaceholder.typicode.com/posts" authToken postData
        ]
        
        // Run all requests in parallel
        let results = 
            requests
            |> Async.Parallel
            |> Async.RunSynchronously
        
        // Print results to console
        results |> Array.iter (fun r -> 
            printfn "%s %s | Status: %d | Size: %d bytes | Time: %dms" 
                    r.Method r.Url r.StatusCode r.ContentLength r.ResponseTime)
        
        // Save to RSS XML file in project root directory
        let projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName
        let filePath = Path.Combine(projectRoot, "results.xml")
        let rssDoc = generateRssXml results
        rssDoc.Save(filePath)
        
        printfn "\nResults saved to results.xml as RSS feed in project directory"
        0