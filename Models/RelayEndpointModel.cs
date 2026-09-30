using System;

namespace cAlgo.Robots;

// Where the relay is, and the key to send it. An empty key sends none.
public class RelayEndpointModel {
    public RelayEndpointModel(Uri url, string accessKey) {
        Url = url;
        AccessKey = accessKey?.Trim() ?? "";
    }

    public Uri Url { get; }

    public string AccessKey { get; }
}
