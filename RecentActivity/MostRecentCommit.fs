module MostRecentCommit

open System.Net.Http
open System.Text.Json
open System.Xml.Linq

// Simple record for push events
type PushEvent = {
    Type: string
    RepoName: string
    CommitMessage: string
    CreatedAt: string
    Sha: string
    Url: string
}

let httpClient = 
    let client = new HttpClient()
    client.DefaultRequestHeaders.Add("User-Agent", "F#-App/1.0")
    client

// Convert PushEvent to RSS item XML
let pushEventToRssItem (pushEvent: PushEvent) =
    let title = sprintf "[%s] %s" pushEvent.RepoName pushEvent.CommitMessage
    let description = sprintf "Commit %s in repository %s" (pushEvent.Sha.[0..6]) pushEvent.RepoName
    let pubDate = System.DateTime.Parse(pushEvent.CreatedAt).ToString("R") // RFC 1123 format
    let guid = pushEvent.Sha
    
    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), pushEvent.Url),
        XElement(XName.Get("guid"), guid),
        XElement(XName.Get("pubDate"), pubDate)
    )

// Get the most recent PushEvent and return as RSS XML
let getMostRecentPushEventAsRss () = async {
    try
        let url = "https://api.github.com/users/joe307bad/events/public"
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        
        let jsonDoc = JsonDocument.Parse(content)
        let events = jsonDoc.RootElement.EnumerateArray()
        
        let mostRecentPushEvent = 
            events
            |> Seq.filter (fun event -> 
                event.GetProperty("type").GetString() = "PushEvent")
            |> Seq.choose (fun event ->
                let repo = event.GetProperty("repo").GetProperty("name").GetString()
                let createdAt = event.GetProperty("created_at").GetString()
                
                // Get commits and take only the most recent one
                let commits = event.GetProperty("payload").GetProperty("commits").EnumerateArray()
                if commits |> Seq.isEmpty then
                    None
                else
                    let mostRecentCommit = commits |> Seq.head
                    let message = mostRecentCommit.GetProperty("message").GetString()
                    let sha = mostRecentCommit.GetProperty("sha").GetString()
                    let url = mostRecentCommit.GetProperty("url").GetString()
                    
                    Some {
                        Type = "PushEvent"
                        RepoName = repo
                        CommitMessage = message
                        CreatedAt = createdAt
                        Sha = sha
                        Url = url
                    })
            |> Seq.filter (fun pushEvent -> 
                not (pushEvent.CommitMessage.Contains("Badaczewski_CV")))
            |> Seq.tryHead
        
        match mostRecentPushEvent with
        | Some pushEvent -> 
            let rssItem = pushEventToRssItem pushEvent
            return Some rssItem
        | None -> 
            return None
            
    with
    | ex -> 
        printfn "Error: %s" ex.Message
        return None
}

// Get RSS XML as string
let getMostRecentPushEventAsRssString () = async {
    let! rssItem = getMostRecentPushEventAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}