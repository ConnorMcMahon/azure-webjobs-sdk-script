#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, Counter counter, int add)
{
    counter.Value += add;

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
            Content = new StringContent("Updated the value of the counter by " + add + ".")
        };
    }

    return res;

}

public class Counter : TableEntity
{
    public int Value { get; set; }
}