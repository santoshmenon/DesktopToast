﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Foundation;
using Windows.UI.Notifications;

using DesktopToast.Helper;

namespace DesktopToast
{
	/// <summary>
	/// Manages toast notifications.
	/// </summary>
	public class ToastManager
	{
		/// <summary>
		/// Shows a toast.
		/// </summary>
		/// <param name="request">Toast request</param>
		/// <returns>Result of showing a toast</returns>
		public static Task<ToastResult> ShowAsync(ToastRequest request)
		{
			return ShowAsync(request, CancellationToken.None);
		}

		/// <summary>
		/// Shows a toast.
		/// </summary>
		/// <param name="request">Toast request</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result of showing a toast</returns>
		public static async Task<ToastResult> ShowAsync(ToastRequest request, CancellationToken cancellationToken)
		{
			if (request is null)
				throw new ArgumentNullException(nameof(request));

			if (!OsVersion.IsEightOrNewer)
				return ToastResult.Unavailable;

			if (request.IsShortcutValid)
				await CheckInstallShortcut(request);

			if (!request.IsToastValid)
				return ToastResult.Invalid;

			var document = PrepareToastDocument(request);
			if (document is null)
				return ToastResult.Invalid;

			return await ShowBaseAsync(document, request.AppId, cancellationToken);
		}

		/// <summary>
		/// Shows a toast by toast request in JSON format.
		/// </summary>
		/// <param name="requestJson">Toast request in JSON format</param>
		/// <returns>Result of showing a toast</returns>
		public static Task<ToastResult> ShowAsync(string requestJson)
		{
			return ShowAsync(requestJson, CancellationToken.None);
		}

		/// <summary>
		/// Shows a toast by toast request in JSON format.
		/// </summary>
		/// <param name="requestJson">Toast request in JSON format</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result of showing a toast</returns>
		public static async Task<ToastResult> ShowAsync(string requestJson, CancellationToken cancellationToken)
		{
			ToastRequest request;
			try
			{
				request = new ToastRequest(requestJson);
			}
			catch
			{
				return ToastResult.Invalid;
			}

			return await ShowAsync(request, cancellationToken);
		}

		/// <summary>
		/// Shows a toast by toast request in <see cref="Windows.Data.Xml.Dom.XmlDocument"/>.
		/// </summary>
		/// <param name="requestXml">Toast request in XmlDocument</param>
		/// <param name="appId">AppUserModelID</param>
		/// <returns>Result of showing a toast</returns>
		public static Task<ToastResult> ShowAsync(object requestXml, string appId)
		{
			return ShowAsync(requestXml, appId, CancellationToken.None);
		}

		/// <summary>
		/// Shows a toast by toast request in <see cref="Windows.Data.Xml.Dom.XmlDocument"/>.
		/// </summary>
		/// <param name="requestXml">Toast request in XmlDocument</param>
		/// <param name="appId">AppUserModelID</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result of showing a toast</returns>
		public static async Task<ToastResult> ShowAsync(object requestXml, string appId, CancellationToken cancellationToken)
		{
			if (!(requestXml is XmlDocument document))
				throw new ArgumentException(nameof(requestXml));

			if (string.IsNullOrWhiteSpace(appId))
				throw new ArgumentNullException(nameof(appId));

			if (!OsVersion.IsEightOrNewer)
				return ToastResult.Unavailable;

			return await ShowBaseAsync(document, appId, cancellationToken);
		}

		#region Document

		private enum AudioOption
		{
			Silent,
			Short,
			Long,
		}

		/// <summary>
		/// Prepares a toast document.
		/// </summary>
		/// <param name="request">Toast request</param>
		/// <returns>Toast document</returns>
		private static XmlDocument PrepareToastDocument(ToastRequest request)
		{
			XmlDocument document;
			if (!string.IsNullOrWhiteSpace(request.ToastXml))
			{
				// Load a toast document from XML.
				document = new XmlDocument();
				try
				{
					document.LoadXml(request.ToastXml);
				}
				catch
				{
					return null;
				}
			}
			else
			{
				// Compose a toast document.
				document = OsVersion.IsTenOrNewer
					? ComposeVisualForWin10(request)
					: ComposeVisualForWin8(request);

				document = AddAudio(document, request);
			}

			Debug.WriteLine(document.GetXml());

			return document;
		}

		private static XmlDocument ComposeVisualForWin10(ToastRequest request)
		{
			var document = new XmlDocument();
			document.AppendChild(document.CreateElement("toast"));

			var visualElement = document.CreateElement("visual");
			document.DocumentElement.AppendChild(visualElement);

			var bindingElement = document.CreateElement("binding");
			bindingElement.SetAttribute("template", "ToastGeneric");
			visualElement.AppendChild(bindingElement);

			if (!string.IsNullOrWhiteSpace(request.ToastTitle))
			{
				var toastTitle = document.CreateElement("text");
				toastTitle.AppendChild(document.CreateTextNode(request.ToastTitle));
				bindingElement.AppendChild(toastTitle);
			}

			foreach (string body in request.ToastBodyList)
			{
				var toastBody = document.CreateElement("text");
				toastBody.AppendChild(document.CreateTextNode(body));
				bindingElement.AppendChild(toastBody);
			}

			if (!string.IsNullOrWhiteSpace(request.ToastLogoFilePath))
			{
				var appLogo = document.CreateElement("image");
				appLogo.SetAttribute("placement", "appLogoOverride");
				appLogo.SetAttribute("src", request.ToastLogoFilePath);
				bindingElement.AppendChild(appLogo);
			}

			return document;
		}

		private static XmlDocument ComposeVisualForWin8(ToastRequest request)
		{
			var templateType = GetTemplateType(request);

			// Get a toast template.
			var document = ToastNotificationManager.GetTemplateContent(templateType);

			// Fill in image element.
			switch (templateType)
			{
				case ToastTemplateType.ToastImageAndText01:
				case ToastTemplateType.ToastImageAndText02:
				case ToastTemplateType.ToastImageAndText04:
					var imageElements = document.GetElementsByTagName("image");
					imageElements[0].Attributes.GetNamedItem("src").NodeValue = request.ToastLogoFilePath;
					break;
			}

			// Fill in text elements.
			var textElements = document.GetElementsByTagName("text");
			switch (templateType)
			{
				case ToastTemplateType.ToastImageAndText01:
				case ToastTemplateType.ToastText01:
					textElements[0].AppendChild(document.CreateTextNode(request.ToastBodyList[0]));
					break;

				case ToastTemplateType.ToastImageAndText02:
				case ToastTemplateType.ToastText02:
					textElements[0].AppendChild(document.CreateTextNode(request.ToastTitle));
					textElements[1].AppendChild(document.CreateTextNode(request.ToastBodyList[0]));
					break;

				case ToastTemplateType.ToastImageAndText04:
				case ToastTemplateType.ToastText04:
					textElements[0].AppendChild(document.CreateTextNode(request.ToastTitle));
					textElements[1].AppendChild(document.CreateTextNode(request.ToastBodyList[0]));
					textElements[2].AppendChild(document.CreateTextNode(request.ToastBodyList[1]));
					break;
			}

			return document;
		}

		private static ToastTemplateType GetTemplateType(ToastRequest request)
		{
			if (!string.IsNullOrWhiteSpace(request.ToastLogoFilePath))
			{
				if (string.IsNullOrWhiteSpace(request.ToastTitle))
					return ToastTemplateType.ToastImageAndText01;

				return (request.ToastBodyList.Count < 2)
					? ToastTemplateType.ToastImageAndText02
					: ToastTemplateType.ToastImageAndText04;

				// ToastTemplateType.ToastImageAndText03 will not be used.
			}
			else
			{
				if (string.IsNullOrWhiteSpace(request.ToastTitle))
					return ToastTemplateType.ToastText01;

				return (request.ToastBodyList.Count < 2)
					? ToastTemplateType.ToastText02
					: ToastTemplateType.ToastText04;

				// ToastTemplateType.ToastText03 will not be used.
			}
		}

		private static XmlDocument AddAudio(XmlDocument document, ToastRequest request)
		{
			var option = CheckAudio(request.ToastAudio);
			if (option == AudioOption.Long)
				document.DocumentElement.SetAttribute("duration", "long");

			var audioElement = document.CreateElement("audio");
			if (option == AudioOption.Silent)
			{
				audioElement.SetAttribute("silent", "true");
			}
			else
			{
				audioElement.SetAttribute("src", $"ms-winsoundevent:Notification.{request.ToastAudio.ToString().ToCamelWithSeparator('.')}");
				audioElement.SetAttribute("loop", (option == AudioOption.Long) ? "true" : "false");
			}
			document.DocumentElement.AppendChild(audioElement);

			return document;
		}

		private static AudioOption CheckAudio(ToastAudio audio)
		{
			switch (audio)
			{
				case ToastAudio.Silent:
					return AudioOption.Silent;

				case ToastAudio.Default:
				case ToastAudio.IM:
				case ToastAudio.Mail:
				case ToastAudio.Reminder:
				case ToastAudio.SMS:
					return AudioOption.Short;

				default:
					return AudioOption.Long;
			}
		}

		#endregion

		#region Shortcut

		/// <summary>
		/// Waiting duration before showing a toast after the shortcut file is installed
		/// </summary>
		/// <remarks>It seems that roughly 3 seconds are required.</remarks>
		private static readonly TimeSpan _waitingDuration = TimeSpan.FromSeconds(3);

		/// <summary>
		/// Checks and installs a shortcut file in Start menu.
		/// </summary>
		/// <param name="request">Toast request</param>
		private static async Task CheckInstallShortcut(ToastRequest request)
		{
			var shortcutFilePath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), // Not CommonStartMenu
				"Programs",
				request.ShortcutFileName);

			var shortcut = new Shortcut();

			if (!shortcut.CheckShortcut(
				shortcutPath: shortcutFilePath,
				targetPath: request.ShortcutTargetFilePath,
				arguments: request.ShortcutArguments,
				comment: request.ShortcutComment,
				workingFolder: request.ShortcutWorkingFolder,
				windowState: request.ShortcutWindowState,
				iconPath: request.ShortcutIconFilePath,
				appId: request.AppId,
				activatorId: request.ActivatorId))
			{
				shortcut.InstallShortcut(
					shortcutPath: shortcutFilePath,
					targetPath: request.ShortcutTargetFilePath,
					arguments: request.ShortcutArguments,
					comment: request.ShortcutComment,
					workingFolder: request.ShortcutWorkingFolder,
					windowState: request.ShortcutWindowState,
					iconPath: request.ShortcutIconFilePath,
					appId: request.AppId,
					activatorId: request.ActivatorId);

				await Task.Delay((TimeSpan.Zero < request.WaitingDuration) ? request.WaitingDuration : _waitingDuration);
			}
		}

		#endregion

		#region Toast

		/// <summary>
		/// Shows a toast.
		/// </summary>
		/// <param name="document">Toast document</param>
		/// <param name="appId">AppUserModelID</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>Result of showing a toast</returns>
		private static async Task<ToastResult> ShowBaseAsync(XmlDocument document, string appId, CancellationToken cancellationToken)
		{
			// Prepare handlers for toast events.
			var tcs = new TaskCompletionSource<ToastResult>();

			void activated(ToastNotification _, object e) => tcs.SetResult(ToastResult.Activated);
			void dismissed(ToastNotification _, ToastDismissedEventArgs e) => tcs.SetResult(e.Reason.ToToastResult());
			void failed(ToastNotification _, ToastFailedEventArgs e) => tcs.SetResult(ToastResult.Failed);

			var ctr = default(CancellationTokenRegistration);

			// Create a toast.
			var toast = new ToastNotification(document);

			try
			{
				// Register toast events.
				toast.Activated += activated;
				toast.Dismissed += dismissed;
				toast.Failed += failed;

				// Register and check cancellation token.
				ctr = cancellationToken.Register(() => tcs.SetResult(ToastResult.UserCanceled));

				if (cancellationToken.IsCancellationRequested)
					return ToastResult.UserCanceled;

				// Show a toast.
				ToastNotificationManager.CreateToastNotifier(appId).Show(toast);

				// Wait for the result.
				var result = await tcs.Task;

				Debug.WriteLine($"Toast result: {result}");

				return result;
			}
			finally
			{
				toast.Activated -= activated;
				toast.Dismissed -= dismissed;
				toast.Failed -= failed;

				ctr.Dispose();
			}
		}

		#endregion
	}
}