#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, Counter counter)
{
    var queryParams = req.GetQueryNameValuePairs()
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    string incrementBy = null;
    if(!queryParams.TryGetValue("incrementBy", out incrementBy))
    {
        incrementBy = "1";
        
    }
    counter.Value += Int32.Parse(incrementBy);

    HttpResponseMessage res = null;
    if (counter.Value <= 0)
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Failed to update the counter.")
        };
    }
    else
    {
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Updated the value of the counter by " + incrementBy + ".")
        };
    }

    return res;

}

public class Counter : TableEntity
{
    public int Value { get; set; }
}