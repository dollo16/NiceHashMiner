﻿using Microsoft.Toolkit.Win32.UI.Controls.Interop.WinRT;
using Newtonsoft.Json;
using NHM.Common;
using NHMCore;
using NHMCore.Utils;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NiceHashMiner.Views.Login
{
    /// <summary>
    /// Interaction logic for LoginBrowser.xaml
    /// </summary>
    public partial class LoginBrowser : Window
    {
        public LoginBrowser()
        {
            InitializeComponent();
            Closing += (s, e) => CancelNavigateAndCheck();
        }

        private CancellationTokenSource _cts;
        private bool _canRefresh = false;
        private DateTime _navigationStart = DateTime.MinValue;

        public bool? LoginSuccess { get; private set; } = null;

        private bool? _isOnLoginPage { get; set; } = null;

        private void Browser_Loaded(object sender, RoutedEventArgs e)
        {
            WebViewBrowser.NavigationCompleted += Browser_NavigationCompleted;
            _ = StartNavigateAndCheck();
        }

        private void CancelNavigateAndCheck()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            { }
        }

        private async Task StartNavigateAndCheck()
        {
            LabelStatus.Content = Translations.Tr("Loading, please wait...");
            TryAgainButton.Visibility = Visibility.Collapsed;
            ReturnButton.Visibility = Visibility.Collapsed;
            CancelNavigateAndCheck();
            using (_cts = CancellationTokenSource.CreateLinkedTokenSource(ApplicationStateManager.ExitApplication.Token))
            {
                await NavigateAndCheck(_cts.Token);
            }
        }

        private async Task NavigateAndCheck(CancellationToken stop)
        {
            _navigationStart = DateTime.UtcNow;
            var headers = new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>("User-Agent", "NHM/" + System.Windows.Forms.Application.ProductVersion) };
            WebViewBrowser.NavigationCompleted += Browser_NavigationCompleted;
            WebViewBrowser.Navigate(new Uri(Links.LoginNHM), HttpMethod.Get, null, headers);
            Func<bool> isActive = () => !stop.IsCancellationRequested;
            while (isActive())
            {
                await TaskHelpers.TryDelay(TimeSpan.FromSeconds(1), stop);
                var ok = await CheckForBtc();
                if (!ok.HasValue) continue;

                LoginSuccess = ok;
                Close();
                return;
            }
        }

        private void Browser_NavigationCompleted(object sender, WebViewControlNavigationCompletedEventArgs e)
        {
            Logger.InfoDelayed("Login", $"Navigation to {e.Uri} {e.WebErrorStatus}", TimeSpan.FromSeconds(5));
        }

        private void GoBack_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_canRefresh)
            {
                _canRefresh = false;
                _ = StartNavigateAndCheck();
            }
        }

        const string _jsEvalCode = @"
        (() => {
            try {
                const nhmResponse = document.getElementById('nhmResponse');
                if (!nhmResponse) return 'NO_RESPONSE';
                return nhmResponse.value;
            } catch (error) {
                return 'INJ_ERROR ' + error;
            }
        })();";

        private class Response
        {
            public string btcAddress { get; set; }
            public string error { get; set; }
        }



        private async Task<bool?> CheckForBtc()
        {
            var showRetry = (DateTime.UtcNow - _navigationStart).TotalSeconds > 5;
            string htmlEvalValue = null;
            try
            {
#warning handle case for logged in sessions with/without btc

                htmlEvalValue = await WebViewBrowser.InvokeScriptAsync("eval", _jsEvalCode);
                Logger.InfoDelayed("Login", $"JS eval returned htmlEvalValue='{htmlEvalValue}'", TimeSpan.FromSeconds(15));
                var noNhmResponse = htmlEvalValue == null || htmlEvalValue == "NO_RESPONSE" || htmlEvalValue.Contains("INJ_ERROR");
                if (showRetry)
                {
                    LabelStatus.Content = Translations.Tr("Something went wrong");
                    TryAgainButton.Visibility = Visibility.Visible;
                    ReturnButton.Visibility = Visibility.Visible;
                }
                if (noNhmResponse) return null;


                if (LoadingPanel.Visibility != Visibility.Collapsed)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    WebViewGrid.Visibility = Visibility.Visible;
                }

                var webResponse = JsonConvert.DeserializeObject<Response>(htmlEvalValue);
                if (webResponse == null) return null;

                if (webResponse.btcAddress != null)
                {
                    var result = await ApplicationStateManager.SetBTCIfValidOrDifferent(webResponse.btcAddress);
                    if (result == ApplicationStateManager.SetResult.CHANGED)
                    {
                        Logger.Info("Login", $"Navigation and processing successfull.");
                        return true;
                    }
                    else
                    {
                        Logger.Error("Login", $"Btc address: {webResponse.btcAddress} was not saved. Result: {result}.");
                    }
                }
                else if (webResponse.error != null)
                {
                    var error = webResponse.error;
                    Logger.Error("Login", "Received error: " + error);
                }
                else
                {
                    Logger.Info("Login", $"Navigation and processing successfull. BTC wasn't retreived.");
                }
                return false;
            }
            catch (Exception e)
            {
                if (showRetry)
                {
                    LabelStatus.Content = Translations.Tr("Something went wrong");
                    TryAgainButton.Visibility = Visibility.Visible;
                    ReturnButton.Visibility = Visibility.Visible;
                }
                Logger.ErrorDelayed("Login", $"CheckForBtc error: {e}. htmlEvalValue='{htmlEvalValue}'", TimeSpan.FromSeconds(15));
                return null;
            }
        }
    }
}
