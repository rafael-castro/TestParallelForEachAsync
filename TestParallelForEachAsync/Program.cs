// See https://aka.ms/new-console-template for more information;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;

using AngleSharp;


[DisassemblyDiagnoser(maxDepth: 0)] // change to 0 for just the [Benchmark] method
[MemoryDiagnoser(displayGenColumns: false)]
public class Program
{
    private HttpClient _httpClient;
    private IBrowsingContext _context;
    private int _maxLevel;
    private int _maxTasks;
    private string _userAgent = "";

    //public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);


    public static async Task Main(string[] args)
    {
        await new Program().Test1();
    }

    public Program()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        _context = BrowsingContext.New(Configuration.Default);
        _maxLevel = 2;
        _maxTasks = 4;
    }

    [Benchmark]
    public async Task Test1()
    {
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var cancellationToken = cancellationTokenSource.Token;
        await GetUrlsWithParallelForEachAsync(new[] {
            new UrlWithLevel(""),
        }, cancellationToken);
    }

    [Benchmark]
    public async Task Test2()
    {
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var cancellationToken = cancellationTokenSource.Token;
        await GetUrlsWithParallelForEach(new[] {
            new UrlWithLevel(""),
        }, cancellationToken);
    }

    public async Task GetUrlsWithParallelForEachAsync(IEnumerable<UrlWithLevel> urls, CancellationToken token)
    {
        //await Task.Delay(5);
        if (urls.Any(e => e.Level <= _maxLevel))
        {
            await Parallel.ForEachAsync(urls, new ParallelOptions() { CancellationToken = token, MaxDegreeOfParallelism = _maxTasks }, async (url, token) =>
            {
                var nextUrls = await ParseUrl(url, token);
                await GetUrlsWithParallelForEachAsync(nextUrls.Where(e => e.Level <= _maxLevel), token);
            });
        }
    }

    public async Task GetUrlsWithParallelForEach(IEnumerable<UrlWithLevel> urls, CancellationToken token)
    {
        //await Task.Delay(5);
        if (urls.Any(e => e.Level <= _maxLevel))
        {
            foreach (var c in urls.Chunk(_maxTasks))
            {
                var tasks = c.Select(url => ParseUrl(url, token));
                var nextUrls = await Task.WhenAll(tasks);
                await GetUrlsWithParallelForEach(nextUrls.SelectMany(e => e).Where(e => e.Level <= _maxLevel), token);
            }
        }
    }

    public async Task<IEnumerable<UrlWithLevel>> ParseUrl(UrlWithLevel url, CancellationToken token)
    {
        var baseUri = new Uri(url.Url);

        var response = await _httpClient.GetAsync(url.Url);
        var source = await response.Content.ReadAsStreamAsync(token);
        var document = await _context.OpenAsync(req => req.Content(source), token);
        var a = document.QuerySelectorAll("a[href]");
        return a
            .Select(e => e.GetAttribute("href")!)
            .Where(e => !string.IsNullOrEmpty(e) && !e.StartsWith("#"))
            .Select(e =>
            {
                var childUrl = e;

                if (childUrl.StartsWith("//"))
                {
                    childUrl = $"{baseUri.Scheme}:{e}";
                }
                else if (childUrl.StartsWith("/"))
                {
                    childUrl = $"{baseUri.Scheme}://{baseUri.Host}{e}";
                }

                return childUrl;
            })
            .Where(e => e.StartsWith($"{baseUri.Scheme}://{baseUri.Host}"))
            .Select(e => new UrlWithLevel(e, url.Level + 1));
    }
}

public record UrlWithLevel(string Url, int Level = 1);
