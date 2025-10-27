module MostRecentCommit

open System.Net.Http
open System.Xml.Linq
open System.Text.Json

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

let pushEventToRssItem (pushEvent: PushEvent) =
    let title = sprintf "[%s] %s" pushEvent.RepoName pushEvent.CommitMessage
    let description = sprintf "Commit %s in repository %s" (pushEvent.Sha.[0..6]) pushEvent.RepoName
    let pubDate = System.DateTime.SpecifyKind(System.DateTime.Parse(pushEvent.CreatedAt), System.DateTimeKind.Utc)
    let guid = pushEvent.Sha

    XElement(XName.Get("item"),
        XElement(XName.Get("title"), title),
        XElement(XName.Get("description"), description),
        XElement(XName.Get("link"), pushEvent.Url),
        XElement(XName.Get("guid"), guid),
        XElement(XName.Get("pubDate"), pubDate),
        XElement(XName.Get("contentType"), "code-commit"),
        XElement(XName.Get("repo"), pushEvent.RepoName),
        XElement(XName.Get("commitMessage"), pushEvent.CommitMessage)
    )

let getCommitDetails (repoName: string) (sha: string) = async {
    try
        let url = sprintf "https://api.github.com/repos/%s/commits/%s" repoName sha
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask

        if not response.IsSuccessStatusCode then
            printfn "Failed to get commit details for %s/%s: %A" repoName sha response.StatusCode
            return None
        else
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            let commitJson = JsonDocument.Parse(content)
            let commit = commitJson.RootElement

            let message = commit.GetProperty("commit").GetProperty("message").GetString()
            let commitDate = commit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString()
            let htmlUrl = commit.GetProperty("html_url").GetString()

            return Some (message, commitDate, htmlUrl)
    with
    | ex ->
        printfn "Error getting commit details: %s" ex.Message
        return None
}

let getMostRecentPushEventAsRss () = async {
    try
        let url = "https://api.github.com/users/joe307bad/events"
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        let jsonDoc = JsonDocument.Parse(content)
        let events = jsonDoc.RootElement.EnumerateArray()

        // Get all push events excluding joebad.com repo
        let mostRecentPushEvent =
            events
            |> Seq.filter (fun event ->
                try
                    event.GetProperty("type").GetString() = "PushEvent"
                with
                | _ -> false)
            |> Seq.filter (fun event ->
                try
                    let repoName = event.GetProperty("repo").GetProperty("name").GetString()
                    repoName <> "joe307bad/joebad.com"
                with
                | _ -> false)
            |> Seq.tryHead

        // Only fetch commit details for the most recent push event
        match mostRecentPushEvent with
        | Some event ->
            try
                let repoName = event.GetProperty("repo").GetProperty("name").GetString()
                let sha = event.GetProperty("payload").GetProperty("head").GetString()

                // Get commit details from API
                let! commitDetails = getCommitDetails repoName sha

                match commitDetails with
                | Some (message, commitDate, htmlUrl) ->
                    let pushEvent = {
                        Type = "PushEvent"
                        RepoName = repoName
                        CommitMessage = message
                        CreatedAt = commitDate
                        Sha = sha
                        Url = htmlUrl
                    }
                    let rssItem = pushEventToRssItem pushEvent
                    return Some rssItem
                | None ->
                    return None
            with
            | ex ->
                printfn "Error processing push event: %s" ex.Message
                return None
        | None ->
            printfn "No push events found"
            return None

    with
    | ex ->
        printfn "Error: %s" ex.Message
        return None
}
let getMostRecentPushEventAsRssString () = async {
    let! rssItem = getMostRecentPushEventAsRss()
    match rssItem with
    | Some item -> return Some (item.ToString())
    | None -> return None
}