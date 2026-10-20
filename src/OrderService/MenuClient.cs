using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderService;

// Consumer-owned HTTP contract: no reference to MenuService's assembly or DbContext.
public sealed record MenuProduct(
    [property: JsonRequired] int MenuItemId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] decimal Price,
    [property: JsonRequired] bool IsAvailable);

public sealed class MenuUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class MenuClient(HttpClient httpClient, ILogger<MenuClient> logger)
{
    public async Task<Dictionary<int, MenuProduct?>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        // One deadline covers the entire menu validation, not five seconds per order line.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(httpClient.Timeout);
        var products = new Dictionary<int, MenuProduct?>();
        try
        {
            foreach (var id in ids)
            {
                logger.LogInformation("Retrieving menu item {MenuItemId} over HTTP", id);
                using var response = await httpClient.GetAsync($"/api/menu/{id}", deadline.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    products[id] = null;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var product = await response.Content.ReadFromJsonAsync<MenuProduct>(deadline.Token);
                if (product is null || product.MenuItemId != id || string.IsNullOrWhiteSpace(product.Name)
                    || product.Name.Length > 100 || product.Price < 0 || product.Price > 1000
                    || decimal.Round(product.Price, 2) != product.Price)
                    throw new MenuUnavailableException("MenuService returned invalid product data.");
                products[id] = product;
            }
            return products;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MenuUnavailableException("MenuService timed out. No order was created; try again later.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new MenuUnavailableException("MenuService could not be reached or returned an invalid response. No order was created; try again later.", ex);
        }
    }
}
