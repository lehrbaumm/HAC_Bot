﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Text;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Roboto
{
    /// <summary>
    /// Methods that interact with the Telegram APIs
    /// </summary>
    public static class TelegramAPI
    {
        /// <summary>
        /// Send a message. Returns the ID of the send message
        /// </summary>
        /// <param name="chatID">User or Chat ID</param>
        /// <param name="text"></param>
        /// <param name="markDown"></param>
        /// <param name="replyToMessageID"></param>
        /// <returns>An integer specifying the message id. -1 indicates it is queueed, int.MinValue indicates a failure</returns>
        public static long SendMessage(long chatID, string text, bool markDown = false, long replyToMessageID = -1, bool clearKeyboard = false, bool trySendImmediately = false)
        {
            
            bool isPM = (chatID < 0 ? false : true);
            ExpectedReply e = new ExpectedReply(chatID, chatID, text, isPM , null, null, replyToMessageID, false, "", markDown, clearKeyboard, false);
            
            //add the message to the stack. If it is sent, get the messageID back.
            long messageID = Roboto.Settings.newExpectedReply(e, trySendImmediately);
            return messageID;

        }
        

        /// <summary>
        /// 
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="caption"></param>
        /// <param name="image"></param>
        /// <param name="fileName"></param>
        /// <param name="fileContentType"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="clearKeyboard"></param>
        /// <returns></returns>
        public static long SendPhoto(long chatID, string caption, Stream image, string fileName, string fileContentType, long replyToMessageID, bool clearKeyboard)
        {
            //TODO - should be cached in the expectedReply object first. 
            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendPhoto";

            var pairs = new NameValueCollection();
            pairs["chat_id"] = chatID.ToString();
            pairs["caption"] = caption;

            if (caption.Length > 2000) { caption = caption.Substring(0, 1990); }
            if (replyToMessageID != -1) { pairs["reply_to_message_id"] = replyToMessageID.ToString(); }
            if (clearKeyboard) { pairs["reply_markup"] = "{\"hide_keyboard\":true}"; }
            try
            {
                JObject response = sendPOST(postURL, pairs, image, fileName, fileContentType).Result;
                //get the message ID
                int messageID = response.SelectToken("result.message_id").Value<int>();
                return messageID;
            }
            catch (WebException e)
            {
                //log it and carry on
                Roboto.log.log("Couldnt send photo " + fileName + " to " + chatID + "! " + e.ToString(), logging.loglevel.critical);
            }

            return -1;

            
        }

        /// <summary>
        /// Send a message, which we are expecting a reply to. Message can be sent publically or privately. Replies will be detected and sent via the plugin replyRecieved method. 
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="text"></param>
        /// <param name="replyToMessageID"></param>
        /// <param name="selective"></param>
        /// <param name="answerKeyboard"></param>
        /// <returns>An integer specifying the message id. -1 indicates it is queueed, long.MinValue indicates a failure</returns>
        public static long GetExpectedReply(long chatID, long userID, string text, bool isPrivateMessage, Type pluginType, string messageData, long replyToMessageID = -1, bool selective = false, string answerKeyboard = "", bool useMarkdown = false, bool clearKeyboard = false, bool trySendImmediately = false)
        {
            ExpectedReply e = new ExpectedReply(chatID, userID, text, isPrivateMessage, pluginType, messageData, replyToMessageID, selective, answerKeyboard, useMarkdown, clearKeyboard, true );
       
            //add the message to the stack. If it is sent, get the messageID back.
            long messageID = Roboto.Settings.newExpectedReply(e, trySendImmediately);
            return messageID;
        }

        

        /// <summary>
        /// Send the message in the expected reply. Should only be called from the expectedReply Class. May or may not expect a reply. 
        /// </summary>
        /// <param name="e"></param>
        /// <returns>A long specifying the message id. long.MinValue indicates a failure</returns>
        public static long postExpectedReplyToPlayer(ExpectedReply e)
        {

            Roboto.Settings.stats.logStat(new statItem("Outgoing Msgs", typeof(TelegramAPI)));

            string postURL = Roboto.Settings.telegramAPIURL + Roboto.Settings.telegramAPIKey + "/sendMessage";

            //assemble collection of name/value data
            var pairs = new NameValueCollection();
            string chatID = e.isPrivateMessage ? e.userID.ToString() : e.chatID.ToString(); //send to chat or privately
            try
            {
                pairs.Add("chat_id", chatID);
                if (e.text.Length > 1950) { e.text = e.text.Substring(0, 1950); }


                //check if the user has participated in multiple chats recently, so we can stamp the message with the current chat title. 
                //only do this where the message relates to a chat. The chat ID shouldnt = the user id if this is the case. 
                if (e.isPrivateMessage && e.chatID != e.userID && e.chatID < 0)
                {
                    int nrChats = Roboto.Settings.getChatPresence(e.userID).Count();
                    if (nrChats > 1)
                    {
                        //get the current chat;
                        chat c = Roboto.Settings.getChat(e.chatID);
                        if (c == null)
                        {
                            Roboto.log.log("Couldnt find chat for " + e.chatID + " - did you use the userID accidentally?", logging.loglevel.high);
                        }
                        else
                        {
                            if (e.markDown && c.chatTitle != null) { e.text = "*" + c.chatTitle + "* :" + "\r\n" + e.text; }
                            else { e.text = "=>" + c.chatTitle + "\r\n" + e.text; }
                        }
                    }
                }
                pairs.Add("text", e.text);

                if (e.markDown) { pairs["parse_mode"] = "Markdown"; }

            }
            catch (Exception ex)
            {
                Roboto.log.log("Error assembling message!. " + ex.ToString(), logging.loglevel.critical);
            }
            try //TODO - cant see how this is erroring here. Added try/catch to try debug it.
            {
                //force a reply if we expect one, and the keyboard is empty
                if (e.expectsReply && (e.keyboard == null || e.keyboard == ""))

                {
                    bool forceReply = (!e.isPrivateMessage);

                    //pairs.Add("reply_markup", "{\"force_reply\":true,\"selective\":" + e.selective.ToString().ToLower() + "}");
                    pairs.Add("reply_markup", "{\"force_reply\":"
                        //force reply if we are NOT in a PM
                        + forceReply.ToString().ToLower()
                        //mark selective if passed in
                        + ",\"selective\":" + e.selective.ToString().ToLower() + "}");
                }

                else if (e.clearKeyboard) { pairs["reply_markup"] = "{\"hide_keyboard\":true}"; }
                else if (e.keyboard != null && e.keyboard != "")
                {
                    pairs.Add("reply_markup", "{" + e.keyboard + "}");
                }
                
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error assembling message pairs. " + ex.ToString(), logging.loglevel.high);
            }
            try //TODO - cant see how this is erroring here. Added try/catch to try debug it.
            {
                if (e.replyToMessageID != -1)
                {
                    pairs.Add("reply_to_message_id", e.replyToMessageID.ToString());
                }
            }
            catch (Exception ex)
            {
                //if we failed to attach, it probably wasnt important!
                Roboto.log.log("Error attaching Reply Message ID to message. " + ex.ToString() , logging.loglevel.high); 
            }

            //TODO - should URLEncode the text.
            try
            {
                JObject response = sendPOST(postURL, pairs).Result;

                if (response != null)
                {
                    JToken response_token = response.SelectToken("result");
                    if (response_token != null)
                    {
                        JToken messageID_token = response.SelectToken("result.message_id");
                        if (messageID_token != null)
                        {
                            int messageID = messageID_token.Value<int>();
                            return messageID;
                        }
                        else { Roboto.log.log("MessageID Token was null.", logging.loglevel.high); }
                    }
                    else { Roboto.log.log("Response Token was null.", logging.loglevel.high); }
                }
                else { Roboto.log.log("Response was null.", logging.loglevel.high); }

                Roboto.Settings.parseFailedReply(e);
                return long.MinValue;
            }
            catch (WebException ex)
            {
                Roboto.log.log("Couldnt send message to " + chatID.ToString() + " because " + ex.ToString(), logging.loglevel.high);
                
                //Mark as failed and return the failure to the calling method
                Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType.ToString() + " as failed.", logging.loglevel.high);
                Roboto.Settings.parseFailedReply(e);
                
                return long.MinValue;
            }

            catch (Exception ex)
            {
                Roboto.log.log("Exception sending message to " + chatID.ToString() + " because " + ex.ToString(), logging.loglevel.high);

                //Mark as failed and return the failure to the calling method
                Roboto.log.log("Returning message " + e.messageData + " to plugin " + e.pluginType.ToString() + " as failed.", logging.loglevel.high);
                Roboto.Settings.parseFailedReply(e);

                return long.MinValue;

            }

        }

        

        /// <summary>
        /// Sends a POST message, returns the reply object
        /// </summary>
        /// <param name="postURL"></param>
        /// <returns></returns>
        public static async System.Threading.Tasks.Task<JObject> sendPOST(String postURL, NameValueCollection pairs, Stream image = null, string fileName = null, string fileContentType = null)
        {

            var uri = new Uri(postURL);
            string logtxt = "";
            string responseObject = "";
            using (var client = new HttpClient())
            {

                try
                {
                    HttpResponseMessage response;


                    using (var form = new MultipartFormDataContent())
                    {
                        foreach (string itemKey in pairs)
                        {
                            form.Add(ConvertParameterValue(pairs[itemKey]), itemKey);
                            logtxt += itemKey + " = " + pairs[itemKey] + ". ";

                        }

                        if (image != null)
                        {
                            image.Seek(0, SeekOrigin.Begin);
                            HttpContent c = new StreamContent(image);
                            logtxt += "Image " + fileName + "added. ";
                            form.Add(c, "photo", fileName);
                        }

                        Roboto.log.log("Sending Message: " + postURL + "\n\r" + logtxt , logging.loglevel.low);

                        response = await client.PostAsync(uri, form).ConfigureAwait(false);

                    }
                    responseObject = await response.Content.ReadAsStringAsync();
                    
                }
                catch (HttpRequestException e) 
                {
                    Roboto.log.log("Unable to send Message due to HttpRequestException error:\n\r" + e.ToString(), logging.loglevel.high);
                }
                catch (Exception e)
                {
                    Roboto.log.log("Unable to send Message due to unknown error:\n\r" + e.ToString(), logging.loglevel.critical);
                }

                if (responseObject == null || responseObject == "")
                {
                    Roboto.log.log("Sent message but recieved blank reply confirmation" , logging.loglevel.critical);
                    return null;
                }
                try
                {
                    Roboto.log.log("Result: " + responseObject, logging.loglevel.verbose);
                    JObject jo = JObject.Parse(responseObject);
                    if (jo != null)
                    {
                        return jo;
                    }
                    else
                    {
                        Roboto.log.log("JObject response object was null!", logging.loglevel.critical);
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Roboto.log.log("Couldnt parse response from Telegram when sending message" + e.ToString(), logging.loglevel.critical);
                    Roboto.log.log("Response was: " + responseObject, logging.loglevel.critical);
                    return null;
                }
            }
            
        }


        private static HttpContent ConvertParameterValue(object value)
        {
            var type = value.GetType();

            switch (type.Name)
            {
                case "String":
                case "Int32":
                    return new StringContent(value.ToString());
                case "Boolean":
                    return new StringContent((bool)value ? "true" : "false");
                
                default:
                    var settings = new JsonSerializerSettings
                    {
                        DefaultValueHandling = DefaultValueHandling.Ignore,
                    };

                    return new StringContent(JsonConvert.SerializeObject(value, settings));
            }
        }

        /// <summary>
        /// Writes string to stream. Author : Farhan Ghumra
        /// http://stackoverflow.com/questions/19954287/how-to-upload-file-to-server-with-http-post-multipart-form-data
        /// </summary>
        private static void WriteToStream(Stream s, string txt )
        {
            Roboto.log.log( txt, logging.loglevel.verbose, ConsoleColor.White, false, false, false, true);
            byte[] bytes = Encoding.UTF8.GetBytes(txt);
            s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Writes byte array to stream. Author : Farhan Ghumra
        /// http://stackoverflow.com/questions/19954287/how-to-upload-file-to-server-with-http-post-multipart-form-data
        /// </summary>
        private static void WriteToStream(Stream s, byte[] bytes)
        {
            s.Write(bytes, 0, bytes.Length);
        }


        public static string createKeyboard(List<string> options, int width)
        {
            //["Answer1"],["Answer3"],["Answer12"],["Answer1"],["Answer3"],["Answer12"]
            string reply = "\"keyboard\":[";
            int column = 0;
            int pos = 0;
            foreach (String s in options)
            {
                //String s_clean = HttpUtility.HtmlEncode(s.Trim());
                String s_clean = JsonConvert.SerializeObject(s.Trim());

                //first element
                if (column == 0)
                {
                    reply += "[";
                }
                else
                {
                    reply += ",";
                }

                reply += s_clean;

                column++;
                //last element
                if (column == width && pos != options.Count - 1)
                {
                    column = 0;
                    reply += "],";
                }
                //very final element, 
                else if (pos == options.Count - 1)
                {
                    reply += "]";
                }

                pos++;
            }
            reply += "],\"one_time_keyboard\":true,\"resize_keyboard\":true";

            return reply;
        }


    }
}
