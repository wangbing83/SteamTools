using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.Application.Models;
using System.Application.UI;
using System.Application.UI.Resx;
using System.Application.UI.ViewModels;
using System.Linq;
using System.Properties;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace System.Application.Services
{
    public sealed class UserService : ReactiveObject
    {
        static UserService? mCurrent;

        public static UserService Current => mCurrent ?? new();

        readonly IUserManager userManager = IUserManager.Instance;
        readonly ICloudServiceClient csc = ICloudServiceClient.Instance;
        readonly IHttpService httpService = IHttpService.Instance;
        readonly ISteamworksWebApiService steamworksWebApiService = ISteamworksWebApiService.Instance;
        readonly IWindowManager windowManager = IWindowManager.Instance;

        public async void ShowWindow(CustomWindow windowName)
        {
            var isDialog = true;
            switch (windowName)
            {
                case CustomWindow.LoginOrRegister:
                    {
                        var cUser = await userManager.GetCurrentUserAsync();
                        if (cUser.HasValue()) return;
                        isDialog = false;
                        break;
                    }

                case CustomWindow.UserProfile:
                    isDialog = false;
                    break;
                case CustomWindow.Notice:
                    isDialog = false;
                    break;
                case CustomWindow.ChangeBindPhoneNumber:
                    {
                        var cUser = await userManager.GetCurrentUserAsync();
                        if (!cUser.HasValue()) return;
                        if (string.IsNullOrWhiteSpace(cUser!.PhoneNumber)) return;
                        break;
                    }
            }
            await windowManager.ShowDialog(windowName, isDialog: isDialog, resizeMode: default);
        }

        public void NavigateUserCenterPage()
        {
            if (IViewModelManager.Instance.MainWindow is MainWindowViewModel main)
            {
                main.SelectedItem = new AccountPageViewModel();
            }
        }

        public async Task SignOutAsync(Func<Task<IApiResponse>>? apiCall = null, string? message = null)
        {
            if (User == null) return;
            if (apiCall != null)
            {
                var rsp = await apiCall();
                if (!rsp.IsSuccess)
                {
                    return;
                }
            }
            await SignOutUserManagerAsync();
            await RefreshUserAsync();
            if (message != null)
            {
                Toast.Show(message);
            }
        }

        public async void SignOut()
        {
            if (!IsAuthenticated) return;
            var r = await MessageBox.ShowAsync(AppResources.User_SignOutTip, button: MessageBox.Button.OKCancel);
            if (r == MessageBox.Result.OK)
            {
                await SignOutAsync(csc.Manage.SignOut);
            }
        }

        public async Task SignIn()
        {
            if (User == null)
            {
                return;
            }
            if (User.IsSignIn == false)
            {
                var state = await csc.AccountClockIn();
                if (state.IsSuccess)
                {
                    User.Experience = state.Content!.Experience;
                    User.NextExperience = state.Content!.NextExperience;
                    User.Level = state.Content!.Level;
                    User.EngineOil = state.Content!.Strength;
                    User.IsSignIn = true;
                    //_ = Task.Run(async () =>
                    //{
                    //    await Task.Delay(DateTime.Now.Date.AddDays(1).Subtract(DateTime.Now));
                    //    IsSignedIn = false;
                    //});
                    Toast.Show(AppResources.User_SignIn_Ok);
                }
                else
                {
                    Toast.Show(state.Message);
                }
            }
        }

        public async Task DelAccountAsync()
        {
            if (!IsAuthenticated) return;
            var msg = AppResources.Success_.Format(AppResources.DelAccount);
            await SignOutAsync(csc.Manage.DeleteAccount, msg);
        }

        public async Task SignOutUserManagerAsync()
        {
            await userManager.SignOutAsync();
        }

        [Reactive]
        public UserInfoDTO? User { get; set; }

        /// <summary>
        /// 指示当前用户是否已通过身份验证（已登录）
        /// </summary>
        public bool IsAuthenticated => User != null;

        [Reactive]
        public SteamUser? CurrentSteamUser { get; set; }

        object? _AvatarPath;

        public object? AvatarPath
        {
            get => _AvatarPath;
            set => this.RaiseAndSetIfChanged(ref _AvatarPath, value);
        }

        private UserService()
        {
            mCurrent = this;

            this.WhenAnyValue(x => x.User)
                  .Subscribe(_ => this.RaisePropertyChanged(nameof(AvatarPath)));

            userManager.OnSignOut += () =>
            {
                User = null;
                CurrentSteamUser = null;
                AvatarPath = null;
            };

            Task.Run(Initialize).ForgetAndDispose();
        }

        async void Initialize()
        {
            await RefreshUserAsync();
        }

        [Reactive]
        /// <summary>
        /// 当前登录用户是否有手机号码
        /// </summary>
        public bool HasPhoneNumber { get; set; }

        string _PhoneNumber = string.Empty;

        /// <summary>
        /// 用于 UI 显示的当前登录用户的手机号码(隐藏中间四位)
        /// </summary>
        public string PhoneNumber
        {
            get => _PhoneNumber;
            set => this.RaiseAndSetIfChanged(ref _PhoneNumber, value);
        }

        static string GetCurrentUserPhoneNumber(CurrentUser? user, bool notHideMiddleFour = false)
        {
            var phone_number = user?.PhoneNumber;
            if (string.IsNullOrWhiteSpace(phone_number)) return AppResources.Unbound;
            return notHideMiddleFour ? phone_number : PhoneNumberHelper.ToStringHideMiddleFour(phone_number);
        }

        public void RefreshCurrentUser(CurrentUser? currentUser)
        {
            PhoneNumber = GetCurrentUserPhoneNumber(currentUser);
            HasPhoneNumber = !string.IsNullOrWhiteSpace(currentUser?.PhoneNumber);
        }

        public async Task SaveUserAsync(UserInfoDTO user)
        {
            await userManager.SetCurrentUserInfoAsync(user, true);

            await RefreshUserAsync(user, refreshCurrentUser: false);
        }

        public async Task RefreshUserAsync(UserInfoDTO? user, bool refreshCurrentUser = true)
        {
            User = user;
            this.RaisePropertyChanged(nameof(IsAuthenticated));

            if (refreshCurrentUser)
            {
                var currentUser = await userManager.GetCurrentUserAsync();
                RefreshCurrentUser(currentUser);
            }

            await RefreshUserAvatarAsync();
        }

        public async Task RefreshUserAsync(bool refreshCurrentUser = true)
        {
            var user = await userManager.GetCurrentUserInfoAsync();
            await RefreshUserAsync(user, refreshCurrentUser);
        }

        public async Task RefreshUserAvatarAsync()
        {
            if (User != null)
            {
                if (User.AvatarUrl.Any_Nullable())
                {
                    var settingPriority = FastLoginChannel.Steam; // 设置中优先选取头像渠道配置项
                    var order = new[] { settingPriority }.Concat(new[]
                    {
                        FastLoginChannel.Steam,
                        FastLoginChannel.QQ,
                        FastLoginChannel.Apple,
                        FastLoginChannel.Microsoft,
                    }.Where(x => x != settingPriority));
                    foreach (var item in order)
                    {
                        if (User.AvatarUrl!.ContainsKey(item))
                        {
                            var avatarUrl = User.AvatarUrl[item];
                            if (!string.IsNullOrWhiteSpace(avatarUrl))
                            {
                                var avatarLocalFilePath = await httpService.GetImageAsync(avatarUrl, ImageChannelType.SteamAvatars);
                                var avatarSouce = ImageSouce.TryParse(avatarLocalFilePath, isCircle: true);
                                AvatarPath = avatarSouce;
                            }
                            return;
                        }
                        else if (item == FastLoginChannel.Steam
                            && await RefreshSteamUserAvatarAsync())
                        {
                            return;
                        }
                    }
                }
                else if (await RefreshSteamUserAvatarAsync())
                {
                    return;
                }

                async Task<bool> RefreshSteamUserAvatarAsync()
                {
                    if (User != null && User.SteamAccountId.HasValue)
                    {
                        CurrentSteamUser = await steamworksWebApiService.GetUserInfo(User.SteamAccountId.Value);
                        CurrentSteamUser.AvatarStream = httpService.GetImageAsync(CurrentSteamUser.AvatarFull, ImageChannelType.SteamAvatars);
                        var avatarSouce = ImageSouce.TryParse(await CurrentSteamUser.AvatarStream, isCircle: true);
                        AvatarPath = avatarSouce;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            AvatarPath = null;
        }

        /// <summary>
        /// 更新当前登录用户的手机号码
        /// </summary>
        /// <param name="phoneNumber"></param>
        /// <returns></returns>
        public async Task UpdateCurrentUserPhoneNumberAsync(string phoneNumber)
        {
            var user = await userManager.GetCurrentUserAsync();
            if (user == null) return;
            user.PhoneNumber = phoneNumber;
            await userManager.SetCurrentUserAsync(user);
            RefreshCurrentUser(user);
        }

        /// <summary>
        /// 解绑账号后更新
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public async Task UnbundleAccountAfterUpdateAsync(FastLoginChannel channel)
        {
            var user = await userManager.GetCurrentUserInfoAsync();
            if (user == null) return;
            switch (channel)
            {
                case FastLoginChannel.Steam:
                    user.SteamAccountId = null;
                    break;
                case FastLoginChannel.Microsoft:
                    user.MicrosoftAccountEmail = null;
                    break;
                case FastLoginChannel.QQ:
                    user.QQNickName = null;
                    break;
                case FastLoginChannel.Apple:
                    user.AppleAccountEmail = null;
                    break;
                default:
                    return;
            }
            if (user.AvatarUrl != null && user.AvatarUrl.ContainsKey(channel))
            {
                user.AvatarUrl.Remove(channel);
            }
            await userManager.SetCurrentUserInfoAsync(user, true);
            await RefreshUserAsync(user);
        }

        /// <summary>
        /// 绑定账号后更新
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="rsp"></param>
        /// <returns></returns>
        public async Task BindAccountAfterUpdateAsync(FastLoginChannel channel, LoginOrRegisterResponse rsp)
        {
            var user = await userManager.GetCurrentUserInfoAsync();
            if (user == null) return;
            switch (channel)
            {
                case FastLoginChannel.Steam:
                    user.SteamAccountId = rsp.User?.SteamAccountId;
                    break;
                case FastLoginChannel.Microsoft:
                    user.MicrosoftAccountEmail = rsp.User?.MicrosoftAccountEmail;
                    break;
                case FastLoginChannel.QQ:
                    user.QQNickName = rsp.User?.QQNickName;
                    if (string.IsNullOrEmpty(user.NickName)) user.NickName = user.QQNickName ?? "";
                    break;
                case FastLoginChannel.Apple:
                    user.AppleAccountEmail = rsp.User?.AppleAccountEmail;
                    break;
                default:
                    return;
            }
            if (rsp.User != null)
            {
                if (!string.IsNullOrEmpty(rsp.User.NickName) && string.IsNullOrEmpty(user.NickName)) user.NickName = rsp.User.NickName;
                if (rsp.User.Gender != default && user.Gender != rsp.User.Gender) user.Gender = rsp.User.Gender;
                if (rsp.User.AvatarUrl != null && rsp.User.AvatarUrl.ContainsKey(channel))
                {
                    if (user.AvatarUrl == null)
                    {
                        user.AvatarUrl = new()
                        {
                            { channel, rsp.User.AvatarUrl[channel] }
                        };
                    }
                    else if (user.AvatarUrl.ContainsKey(channel))
                    {
                        user.AvatarUrl[channel] = rsp.User.AvatarUrl[channel];
                    }
                    else
                    {
                        user.AvatarUrl.Add(channel, rsp.User.AvatarUrl[channel]);
                    }
                }
            }
            await userManager.SetCurrentUserInfoAsync(user, true);
            await RefreshUserAsync(user);
        }
    }
}