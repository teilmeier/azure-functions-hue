using System.Threading.Tasks;
using Q42.HueApi.Models;

namespace Functions.Hue
{
  public interface IHueState
  {
    void SetAccessToken(AccessTokenResponse accessToken);
    void SetAppKey(string appKey);
    Task<AccessTokenResponse> GetAccessToken();
    Task<string> GetAppKey();
    void Delete();
  }
}