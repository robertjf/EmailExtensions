﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;


using System.Configuration;


using System.IO;
using System.Linq;
using System.Net;
//using System.Net.Configuration;
using System.Net.Mail;
using System.Net.Mime;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Refactored.Email.Models;
using Refactored.Email.Extensions;

#if NETSTANDARD2_0
using Microsoft.Extensions.Configuration;
#endif



/*
 * Copyright 2006-2018 R & E Foster & Associates P/L
 * 
 * Contact: http://refoster.com.au/
 * Email:   mailto:hello@refoster.com.au
 * 
 * This code remains the property of R & E Foster & Associates, and is used under non-exclusive license.
 */
[assembly: AllowPartiallyTrustedCallers]

namespace Refactored.Email {
	/// <summary>
	/// SMTP Email Functionality includes Mail Merge and HTML/Plain text alternate views.
	/// </summary>
	/// <remarks>
	///  <para>Copyright 2006-2018 R &amp; E Foster &amp; Associates P/L </para>
	///  <para>Email:  <a href="mailto:hello@refoster.com.au">hello@refoster.com.au</a> </para>
	/// </remarks>
	public class Email {
		private static string _fieldDelimiters = "{}";
		private static string _baseUrl;

		/// <summary>Enable Linking to Images instead of embedding them</summary>
		/// <remarks>Any image that has a full url will be linked to instead of embedded in the HTML email.
		/// Images with a relative url will can be linked as well if WebBaseUrl is set.
		/// </remarks>
		/// <seealso cref="P:Refactored.Email.Email.WebBaseUrl" />
		public bool LinkWebImages { get; set; }

		/// <summary>
		/// Gets or Sets the field pattern for mail merge templates.
		/// </summary>
		/// <remarks>The default FieldPattern is {{0}}.  Suggested alternatives are &lt;%{0}%&gt; or [{0}].  Note that the field pattern must contain {0}.</remarks>
		public string FieldPattern { get; set; } = "{{0}}";

		/// <summary>
		/// Gets or Sets basic field delimiters used when creating email templates.
		/// </summary>
		/// <remarks>
		///  Specify the Field delimiters used when creating email templates.  This sets the Field Pattern up based on the following rules:
		///      If a single delimiter character is specified, it will be assumed to have been placed at the start and end of the field.
		///      Otherwise, two delimiter characters will be split so that the first is at the start and the second is at the end.
		/// </remarks>
		public string FieldDelimiters {
			get => _fieldDelimiters;
			set {
				if (string.IsNullOrEmpty(value) || value.Trim() == "") {
					return;
				}

				_fieldDelimiters = value;
				if (_fieldDelimiters.Length == 1) {
					FieldPattern = $"{value}{{0}}{value}";
				} else {
					FieldPattern = $"{value[0]}{{0}}{value[1]}";
				}
			}
		}

		/// <summary>
		/// Enables or Disables the SSL protocol.  Disabled by default.
		/// </summary>
		public bool EnableSsl { get; set; }

		/// <summary>
		/// Gets or Sets the base filesystem directory containing mailmerge templates
		/// </summary>
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		public string MailTemplateDirectory { get; set; }

		/// <summary>
		/// Gets or Sets the Http/Https base Url for relative links found in the content templates
		/// </summary>
		/// <seealso cref="P:Refactored.Email.Email.LinkWebImages" />
		public string WebBaseUrl {
			get {



#if NET45
				if (string.IsNullOrEmpty(_baseUrl))
				{
				    HttpContext current = HttpContext.Current;
				    if (current != null)
				        _baseUrl = current.Request.Url.GetLeftPart(UriPartial.Authority);
				}
#elif NETSTANDARD2_0
				//TODO: How do we access HttpContext.Current.Request.Url in .net Core Singleton?
#endif

				return _baseUrl;
			}
			set => _baseUrl = value;
		}




#if NET45
		public Email() {

		}
#elif NETSTANDARD2_0

		public SmtpSection SmtpSettings { get; set; }

		public Email(IConfiguration iConfiguration) {
			var emailSection = iConfiguration.GetSection("Email");
			if (emailSection != null) {

				if (!string.IsNullOrEmpty(emailSection.GetSection("WebBaseUrl")?.Value)) {
					WebBaseUrl = emailSection.GetSection("WebBaseUrl").Value;
				}


				var smtpSection = emailSection.GetSection("SMTP");
				if (smtpSection != null) {
					int port = 25;
					if (!string.IsNullOrEmpty(smtpSection.GetSection("Port")?.Value)) {
						port = int.Parse(smtpSection.GetSection("Port").Value);
					}
					SmtpSettings = new SmtpSection() {
						Host = smtpSection.GetSection("Host").Value,
						Port = port,
						From = smtpSection.GetSection("From").Value,
						Credentials = new NetworkCredential(
							smtpSection.GetSection("Username").Value,
							smtpSection.GetSection("Password").Value
						)
					};
				}
			}
		}


#endif
		/// <summary>
		/// Sends an Email with a Html body and an alternate Text body.
		/// </summary>
		/// <param name="from">From email address</param>
		/// <param name="to">To email addresses, separated by ";"</param>
		/// <param name="subject">Email subject</param>
		/// <param name="htmlBody">HTML formatted content</param>
		/// <param name="plainBody">Plain text formatted content</param>
		/// <param name="cc">CC email addresses, separated by ";"</param>
		/// <param name="bcc">BCC email addresses, separated by ";"</param>
		/// <param name="attachments">List of attachments to add to the email</param>
		/// <param name="replyToList">List of replyTo addresses, separated by ","</param>
		public void SendEmail(string from, string to, string subject, string htmlBody, string plainBody, string cc = "", string bcc = "", IEnumerable<Attachment> attachments = null, string replyToList = "") {
			if (string.IsNullOrEmpty(htmlBody) && string.IsNullOrEmpty(plainBody)) {
				throw new ArgumentException("Please specify a valid message for either htmlBody or plainBody.");
			}

			using (MailMessage message = new MailMessage()) {
				AddMessageAddresses(message, from, to, cc, bcc);
				message.Subject = subject;
				if (!string.IsNullOrEmpty(replyToList)) {
					message.ReplyToList.Add(replyToList);
				}
				SendMessage(PrepareMessage(message, htmlBody, plainBody, attachments));
			}
		}

		/// <summary>
		/// Sends an Email with a Html body and an alternate Text body.
		/// </summary>
		/// <param name="from">From email address</param>
		/// <param name="to">Collection of Email Addresses to send the email to</param>
		/// <param name="subject">Email subject</param>
		/// <param name="htmlBody">HTML formatted content</param>
		/// <param name="plainBody">Plain text formatted content</param>
		/// <param name="cc">Collection of Email Addresses to copy the email to</param>
		/// <param name="bcc">Collection of Email Addresses to "Blind" copy the email to - these won't be seen by the email client.</param>
		/// <param name="attachments">Collection of attachments to add to the message</param>
		public void SendEmail(string from, MailAddressCollection to, string subject, string htmlBody, string plainBody, MailAddressCollection cc = null, MailAddressCollection bcc = null, IEnumerable<Attachment> attachments = null, MailAddressCollection replyTo = null) {
			SendEmail(new MailAddress(from), to, subject, htmlBody, plainBody, cc, bcc, attachments, replyTo);
		}

		/// <summary>
		/// Sends an Email with a Html body and an alternate Text body.
		/// </summary>
		/// <param name="from">From email address</param>
		/// <param name="to">Collection of Email Addresses to send the email to</param>
		/// <param name="subject">Email subject</param>
		/// <param name="htmlBody">HTML formatted content</param>
		/// <param name="plainBody">Plain text formatted content</param>
		/// <param name="cc">Collection of Email Addresses to copy the email to</param>
		/// <param name="bcc">Collection of Email Addresses to "Blind" copy the email to - these won't be seen by the email client.</param>
		/// <param name="attachments">Collection of attachments to add to the message</param>
		public void SendEmail(MailAddress from, MailAddressCollection to, string subject, string htmlBody, string plainBody, MailAddressCollection cc = null, MailAddressCollection bcc = null, IEnumerable<Attachment> attachments = null, MailAddressCollection replyTo = null) {
			if (string.IsNullOrEmpty(htmlBody) && string.IsNullOrEmpty(plainBody)) {
				throw new ArgumentException("Please specify a valid message for either htmlBody or plainBody.");
			}

			using (MailMessage message = new MailMessage()) {
				AddMessageAddresses(message, from, to, cc, bcc);
				message.Subject = subject;
				if (replyTo != null) {
					foreach (MailAddress replyAddress in replyTo) {
						message.ReplyToList.Add(replyAddress);
					}
				}

				SendMessage(PrepareMessage(message, htmlBody, plainBody, attachments));
			}
		}
		/// <summary>
		/// Sends an Email with a Html body and an alternate Text body.
		/// </summary>
		/// <param name="from">From email address</param>
		/// <param name="to">Collection of Email Addresses to send the email to</param>
		/// <param name="subject">Email subject</param>
		/// <param name="htmlBody">HTML formatted content</param>
		/// <param name="plainBody">Plain text formatted content</param>
		/// <param name="cc">Collection of Email Addresses to copy the email to</param>
		/// <param name="bcc">Collection of Email Addresses to "Blind" copy the email to - these won't be seen by the email client.</param>
		/// <param name="attachments">Collection of attachments to add to the message</param>
		public void SendEmail(MailAddress from, MailAddress to, string subject, string htmlBody, string plainBody, MailAddress cc = null, MailAddress bcc = null, IEnumerable<Attachment> attachments = null, MailAddress replyTo = null) {
			SendEmail(from, new MailAddressCollection { to }, subject, htmlBody, plainBody, new MailAddressCollection { cc }, new MailAddressCollection { bcc }, attachments, new MailAddressCollection { replyTo });
		}

		/// <summary>
		/// Prepares an alternate email view.  Email views can be html or plain text.
		/// </summary>
		/// <param name="content">Content to be prepared</param>
		/// <param name="contentType">content type for the email content - either text/html or text/plain</param>
		/// <returns>AlternateView object containing the content provided along with it's content type.</returns>
		public AlternateView PrepareAlternateView(string content, string contentType = "text/plain") {
			if (string.IsNullOrEmpty(content))
				return null;

			AlternateView view;
			if (string.IsNullOrEmpty(contentType)) {
				view = AlternateView.CreateAlternateViewFromString(content);
			} else if (contentType == "text/html") {
				List<LinkedResource> resources = new List<LinkedResource>();
				content = ExtractFiles(content, resources);
				content = ExpandUrls(content);
				content = SanitiseHtml(content);
				view = AlternateView.CreateAlternateViewFromString(content, new ContentType(contentType));
				view.ContentType.CharSet = Encoding.UTF8.WebName;
				if (!string.IsNullOrWhiteSpace(WebBaseUrl)) {
					view.BaseUri = new Uri(WebBaseUrl);
				}

				if (resources.Count > 0) {
					foreach (LinkedResource linkedResource in resources)
						view.LinkedResources.Add(linkedResource);
				}
			} else {
				view = AlternateView.CreateAlternateViewFromString(content, new ContentType(contentType));
			}

			return view;
		}

		/// <summary>
		/// Extension method - prepares the html and plain message body, adding attachments from within the html body as required and returns the updated message.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="htmlBody"></param>
		/// <param name="plainBody"></param>
		/// <param name="attachments"></param>
		/// <returns></returns>
		public MailMessage PrepareMessage(MailMessage message, string htmlBody, string plainBody, IEnumerable<Attachment> attachments = null) {
			if (!string.IsNullOrEmpty(htmlBody)) {
				if (!string.IsNullOrEmpty(plainBody)) {
					AlternateView alternateView = PrepareAlternateView(plainBody, "text/plain");
					message.AlternateViews.Add(alternateView);
				}

				AlternateView alternateView1 = PrepareAlternateView(htmlBody, "text/html");
				message.AlternateViews.Add(alternateView1);
			} else {
				message.Body = plainBody;
				message.IsBodyHtml = false;
			}
			if (attachments != null && attachments.Any()) {
				foreach (Attachment attachment in attachments)
					message.Attachments.Add(attachment);
			}
			return message;
		}

		/// <summary>
		/// Validates that a passed in string is a valid email address format.
		/// </summary>
		/// <param name="email"></param>
		/// <returns></returns>
		public static string ValidateEmailAddress(string email) {
			string msg = string.Empty;
			try {
				MailAddress mailAddress = new MailAddress(email);
			} catch (Exception ex) {
				msg = ex.Message;
			}

			return msg;
		}

		/// <summary>
		/// Retrieves the actual template filename from application settings based on the templateName and the MailTemplateDirectory.
		/// </summary>
		/// <remarks>
		/// appSettings configuration should contain something like the following:
		/// <code>
		/// <![CDATA[
		/// 	<add key="mailTemplateDir" value="~/common/mail_templates/"/>
		/// 	<add key="mtHtmlMembershipActivated" value="membership_activated.html"/>
		/// ]]>
		/// </code>
		/// </remarks>
		/// <param name="templateName"></param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns></returns>
		public string GetMessageTemplate(string templateName) {


#if NET45
			string templateDirectory = HttpContext.Current?.Server?.MapPath(MailTemplateDirectory) ?? MailTemplateDirectory;
			 string template = System.Configuration.ConfigurationManager.AppSettings[templateName];
#elif NETSTANDARD2_0
			//TODO: This only works for Absolute file paths
			string templateDirectory = MailTemplateDirectory;
			string template = ConfigurationManager.AppSettings[templateName];

#endif
			if (string.IsNullOrEmpty(template)) {
				template = templateName;
			}

			if (!templateDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
				!template.StartsWith(Path.DirectorySeparatorChar.ToString())) {
				return $"{templateDirectory}{Path.DirectorySeparatorChar}{template}";
			}

			return $"{templateDirectory}{template}";
		}

		private static string ExtractSubject(string content) {
			string subject = string.Empty;
			Match match = new Regex("<title>(?<pageTitle>.*)</title>", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Singleline).Match(content);
			if (match.Groups.Count > 1) {
				subject = match.Groups["pageTitle"].Value.Trim().Replace(Environment.NewLine, " ").Replace("  ", " ");
				try {
#if NET45
					subject = HttpContext.Current.Server.HtmlDecode(subject);
#elif NETSTANDARD2_0
					subject = HttpUtility.UrlDecode(subject);
#endif

				} catch {
				}
			}
			return subject;
		}

		/// <summary>
		/// Generic method to return the content of the message with mail merged parameters
		/// </summary>
		/// <param name="templateName">Name of the template registered in appSettings configuration - refer to <see cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" /> for more information</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplateContent(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplate<T>(string templateName, T parameters) {
			string subject = string.Empty;
			return ParseMessageTemplate(templateName, parameters, out subject);
		}

		/// <summary>
		/// Generic method to return the content of the message with mail merged parameters
		/// </summary>
		/// <param name="templateName">Name of the template registered in appSettings configuration - refer to <see cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" /> for more information</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <param name="subject">Retrieves the subject of the email from the title if present (looks for <code>&lt;title&gt;&lt;/title&gt;</code> tags)</param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplateContent(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplate<T>(string templateName, T parameters, out string subject) {
			return ParseMessageTemplateContent(File.ReadAllText(GetMessageTemplate(templateName)), parameters, out subject);
		}

		/// <summary>
		/// Returns the content of the message with mail merged parameters
		/// </summary>
		/// <param name="templateName">Name of the template registered in appSettings configuration - refer to <see cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" /> for more information</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplateContent(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplate(string templateName, IDictionary<string, object> parameters) {
			return ParseMessageTemplate(templateName, parameters, out string subject);
		}

		/// <summary>
		/// Returns the content of the message template with mail merged parameters
		/// </summary>
		/// <param name="templateName">Name of the template registered in appSettings configuration - refer to <see cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" /> for more information</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplateContent(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content of the named Template file</returns>
		public string ParseMessageTemplate(string templateName, NameValueCollection parameters) {
			return ParseMessageTemplate(templateName, parameters, out string subject);
		}

		/// <summary>
		/// Returns the content of the message template with mail merged parameters
		/// </summary>
		/// <param name="templateName">Name of the template registered in appSettings configuration - refer to <see cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" /> for more information</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <param name="subject">Retrieves the subject of the email from the title if present (looks for <code>&lt;title&gt;&lt;/title&gt;</code> tags)</param>
		/// <seealso cref="P:Refactored.Email.Email.MailTemplateDirectory" />
		/// <seealso cref="M:Refactored.Email.Email.GetMessageTemplate(System.String)" />
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplateContent(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content of the named Template file</returns>
		public string ParseMessageTemplate(string templateName, NameValueCollection ValueParameters, out string subject) {
			return ParseMessageTemplate(templateName: templateName, parameters: ValueParameters, subject: out subject);
		}

		/// <summary>
		/// Returns the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent(string content, IDictionary<string, object> parameters) {
			return ParseMessageTemplateContent(content, parameters, out string subject);
		}

		/// <summary>
		/// Returns the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data.  Any parameter that contains enumerated data will be rendered as a list.</param>
		/// <param name="subject">Retrieves the subject of the email from the title if present (looks for <code>&lt;title&gt;&lt;/title&gt;</code> tags)</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent(string content, IDictionary<string, object> parameters, out string subject) {
			// Basic check for the existence of HTML content by checking for a <title> tag
			bool isHtml = !string.IsNullOrEmpty(ExtractSubject(content));

			foreach (string key in parameters.Keys) {
				object param = parameters[key];
				// If the parameter is an Enumerable then we can iterate through each item in it and build a list.
				if (!(param is string) && IsEnumerable(param.GetType())) {
					StringBuilder stringBuilder = new StringBuilder();
					foreach (object item in (IEnumerable)param) {
						if (isHtml)
							stringBuilder.AppendFormat("<li>{0}</li>", item);
						else
							stringBuilder.AppendFormat("{0}{1}", item, Environment.NewLine);
					}

					// Replace the original param with the build list for further processing.
					param = !isHtml ? stringBuilder.ToString() : $"<ul>{stringBuilder}</ul>";
				}
				content = content.Replace(string.Format(FieldPattern, key), param.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
			}
			subject = ExtractSubject(content);

			return content;
		}

		/// <summary>
		/// Helper function to determine whether an object is an Enumerable or not.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		private static bool IsEnumerable(Type type) {
			foreach (Type type1 in type.GetInterfaces()) {
				if (type1.IsGenericType && type1.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Generic method to return the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent<T>(string content, T parameters) {
			return ParseMessageTemplateContent(content, parameters, out string subject);
		}

		/// <summary>
		/// Generic method to return the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <param name="subject">Retrieves the subject of the email from the title if present (looks for <code>&lt;title&gt;&lt;/title&gt;</code> tags)</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent<T>(string content, T parameters, out string subject) {
			if ((object)parameters is NameValueCollection) {
				return ParseMessageTemplateContent(content, (object)parameters as NameValueCollection, out subject);
			}

			if (parameters is IDictionary) {
				return ParseMessageTemplateContent(content, (IDictionary)(object)parameters, out subject);
			}

			return ParseMessageTemplateContent(content, ((object)parameters).ToDictionary<object>(), out subject);
		}

		/// <summary>
		/// Returns the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent(string content, NameValueCollection parameters) {
			return ParseMessageTemplateContent(content, parameters, out string subject);
		}

		/// <summary>
		/// Returns the content of the message with mail merged parameters
		/// </summary>
		/// <param name="content">message body to be merged with with mail merge data</param>
		/// <param name="parameters">Collection of field values containing mail merge data</param>
		/// <param name="subject">Retrieves the subject of the email from the title if present (looks for <code>&lt;title&gt;&lt;/title&gt;</code> tags)</param>
		/// <seealso cref="M:Refactored.Email.Email.ParseMessageTemplate(System.String,System.Collections.Generic.IDictionary{System.String,System.Object})" />
		/// <returns>Content that has been parsed and updated</returns>
		public string ParseMessageTemplateContent(string content, NameValueCollection parameters, out string subject) {
			foreach (string key in parameters.Keys) {
				content = content.Replace(string.Format(FieldPattern, key), parameters[key] ?? "", StringComparison.OrdinalIgnoreCase);
			}

			subject = ExtractSubject(content);
			return content;
		}



		/// <summary>Removes any script tags.</summary>
		/// <remarks><para>this method still has to be fully implemented.</para></remarks>
		/// <param name="content">content to be sanitised</param>
		/// <returns>sanitised content</returns>
		private string SanitiseHtml(string content) {
			return content;
		}

		/// <summary>Replaces the url with a standard full url.</summary>
		/// <remarks>
		/// <para>Added the checks for /http* to take into account invalid urls formatted by TinyMCE adding '/' to the start.</para>
		/// </remarks>
		/// <param name="url"></param>
		/// <returns></returns>
		private string ReplaceUrl(string url) {
			if (Uri.IsWellFormedUriString(url, UriKind.Absolute)) {
				return url;
			}

			if (url.StartsWith("~/")) {
				return url.Replace("~/", WebBaseUrl + "/");
			} else if (url.StartsWith("/http:") || url.StartsWith("/https:")) {
				return url.Substring(1);
			} else if (url.StartsWith("/")) {
				return $"{WebBaseUrl}{url}";
			} else {
				return $"{WebBaseUrl}/{url}";
			}

			//url = !url.StartsWith("~/") ? 
			//        (!url.StartsWith("/http:") ? 
			//            (!url.StartsWith("/https:") ? 
			//                (!url.StartsWith("/") ? $"{WebBaseUrl}/{url}" : WebBaseUrl + url) : 
			//                url.Substring(1)) : 
			//            url.Substring(1)) : 
			//        url.Replace("~/", WebBaseUrl + "/");

			//return url;
		}

		/// <summary>
		/// Expands out local urls (those not containing any server/scheme parts) to full urls.
		/// </summary>
		/// <param name="content"></param>
		/// <returns></returns>
		private string ExpandUrls(string content) {
			foreach (Match match in new Regex("(?<fullHref>(href=(?<quote>\"|'))(?<url>[^\"|']+)(\\k<quote>))").Matches(content)) {
				string url = match.Groups["url"].Value;
				string fullHref = match.Groups["fullHref"].Value;

				string newValue = ReplaceUrl(url);
				string replacement = fullHref.Replace(url, newValue);

				if (content.Contains(fullHref) && !(url == newValue)) {
					content = Regex.Replace(content, Regex.Escape(fullHref), replacement, RegexOptions.IgnoreCase);
				}
			}
			return content;
		}

		/// <summary>
		/// Parses the html body for attachments.  Valid attachments are images embedded in html with src or background attributes
		/// </summary>
		/// <param name="content"></param>
		/// <param name="resources"></param>
		/// <returns></returns>
		private string ExtractFiles(string content, List<LinkedResource> resources) {
			void replaceResource(string imgType, string url) {
				if (string.IsNullOrEmpty(url)) {
					return;
				}

				LinkedResource linkedResource = null;
				if (imgType == "jpg") {
					imgType = "jpeg";
				}

				string fullUrl = ReplaceUrl(url);
				if (fullUrl.StartsWith("http://") || fullUrl.StartsWith("https://")) {
					if (!LinkWebImages) {
						try {
							linkedResource = new LinkedResource(WebRequest.Create(fullUrl).GetResponse().GetResponseStream(), $"image/{imgType}");
						} catch {
						}
					}



				} else {
#if NET45
					HttpContext context = HttpContext.Current;
					if (context != null) {
						fullUrl = context.Server.MapPath(fullUrl);
					}

#elif NETSTANDARD2_0

					//TODO: Needs to be an absolute URL in .Net Core. What can we do here
#endif


					try {
						linkedResource = new LinkedResource(fullUrl, $"image/{imgType}");
					} catch {
					}
				}



				if (linkedResource != null) {
					string cid = Guid.NewGuid().ToString("N");
					linkedResource.ContentId = cid;
					resources.Add(linkedResource);
					content = Regex.Replace(content, Regex.Escape(url), $"cid:{cid}", RegexOptions.IgnoreCase);
				} else {
					content = Regex.Replace(content, Regex.Escape(url), fullUrl, RegexOptions.IgnoreCase);
				}
			}

			foreach (Match match in new Regex("(?<fullSrc>((src|background)=(?<quote>\"|'))(?<url>([^\"|']+)(?<imgType>jpg|jpeg|gif|png|bmp)([^\"|']*))(\\k<quote>))",
												RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture).Matches(content)) {
				string type = match.Groups["imgType"].Value;
				string url = match.Groups["url"].Value;
				string source = match.Groups["fullSrc"].Value;
				if (content.Contains(source)) {
					replaceResource(type, url);
				}
			}
			return content;
		}

		/// <summary>
		/// Extension method - Adds the specified addresses to the message and returns the modified message
		/// </summary>
		/// <param name="message"></param>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="cc"></param>
		/// <param name="bcc"></param>
		/// <returns></returns>
		public void AddMessageAddresses(MailMessage message, string from, string to, string cc = null, string bcc = null) {
			if (string.IsNullOrWhiteSpace(from)) {
				// Attempt to get the default From address.
#if NET45
				var smtpSection = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

				if (smtpSection != null) {
					from = smtpSection.From;
				}
#elif NETSTANDARD2_0
				if (!string.IsNullOrEmpty(SmtpSettings.From)) {
					from = SmtpSettings.From;
				}
#endif

			}

			message.From = MailMessageExtensions.FormatAddress(from);
			message.CC.AddAddresses(cc);
			message.Bcc.AddAddresses(bcc);
			message.To.AddAddresses(to);

			//return message;
		}


		/// <summary>
		/// Extension method - Adds the specified addresses to the message and returns the modified message
		/// </summary>
		/// <param name="message"></param>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="cc"></param>
		/// <param name="bcc"></param>
		/// <returns></returns>
		public void AddMessageAddresses(MailMessage message, MailAddress from, MailAddressCollection to, MailAddressCollection cc = null, MailAddressCollection bcc = null) {
			message.From = MailMessageExtensions.FormatAddress(from);
			message.CC.AddAddresses(cc);
			message.Bcc.AddAddresses(bcc);
			message.To.AddAddresses(to);

			//return message;
		}





		private void SendMessage(MailMessage message) {

#if NET45
			new SmtpClient { EnableSsl = EnableSsl
			}.Send(message);
#elif NETSTANDARD2_0
			new SmtpClient {
				EnableSsl = EnableSsl,
				Host = SmtpSettings.Host,
				Port = SmtpSettings.Port,
				Credentials = SmtpSettings.Credentials
			}.Send(message);
#endif



		}




	}
}
