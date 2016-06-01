#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net.Http;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, Counter counter)
{
    HttpResponseMessage res = null;
    if (counter.Value < 0)
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Get was not properly executed.")
        };
    } else
    {
        res = new HttpResponseMessage(HttpStatusCode.OK )
        {
            Content = new StringContent("The counter's value is " + counter.Value)
        };
    }
    
    return res;
}

public class Counter : TableEntity
{
    public int Value { get; set; }
}