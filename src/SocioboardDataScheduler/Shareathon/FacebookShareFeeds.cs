﻿using SocioboardDataScheduler.Helper;
using SocioboardDataScheduler.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.Socioboard.Models.Mongo;
using Domain.Socioboard.Helpers;
using Facebook;
using MongoDB.Bson;
using MongoDB.Driver;
using Socioboard.Twitter.Authentication;
using Socioboard.Twitter.Twitter.Core.TweetMethods;
using Socioboard.Twitter.App.Core;
using Newtonsoft.Json.Linq;

namespace SocioboardDataScheduler.Shareathon
{
    public class FacebookShareFeeds
    {
        public static int noOfthreadRunning = 0;
        public static Semaphore objSemaphore = new Semaphore(5, 10);
        public static int apiHitsCount = 0;
        public static int MaxapiHitsCount = 100;

        public void ScheduleTwitterMessage()
        {
            while (true)
            {
                try
                {
                    DatabaseRepository dbr = new DatabaseRepository();
                    MongoRepository _facebookSharefeeds = new MongoRepository("FacebookPageFeedShare");
                    var result = _facebookSharefeeds.Find<Domain.Socioboard.Models.Mongo.FacebookPageFeedShare>(t => t.scheduleTime.AddMinutes(5) <=DateTime.UtcNow && t.socialmedia == "tw");
                    var task = Task.Run(async () =>
                    {
                        return await result;
                    });
                    IList<Domain.Socioboard.Models.Mongo.FacebookPageFeedShare> lstfbpagefeeds = task.Result.ToList();
                   
                    //lstScheduledMessage = lstScheduledMessage.Where(t => t.profileId.Contains("758233674978426880")).ToList();
                    var newlstScheduledMessage = lstfbpagefeeds.GroupBy(t => t.pageId).ToList();

                    foreach (var items in newlstScheduledMessage)
                    {
                        objSemaphore.WaitOne();
                        noOfthreadRunning++;
                        Thread thread_pageshreathon = new Thread(() => TwitterSchedulemessage(new object[] { dbr, items }));
                        thread_pageshreathon.Name = "schedulemessages thread :" + noOfthreadRunning;
                        thread_pageshreathon.Start();
                        Thread.Sleep(10 * 1000);
                        //while (noOfthreadRunning > 5)
                        //{
                        //    Thread.Sleep(5 * 1000);
                        //}
                        //new Thread(delegate ()
                        //{
                        //    TwitterSchedulemessage(dbr, items);
                        //}).Start();
                    }
                  //  Thread.Sleep(TimeSpan.FromMinutes(1));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("issue in web api calling" + ex.StackTrace);
                    Thread.Sleep(60000);
                }
            }
        }

        private static void TwitterSchedulemessage(object o)
        {
            try
            {
                MongoRepository mongorepo = new Helper.MongoRepository("ContentFeedsShareathon");
                int pageapiHitsCount;
                object[] arr = o as object[];
                FacebookPageFeedShare shareathon = (FacebookPageFeedShare)arr[0];
                Model.DatabaseRepository dbr = (Model.DatabaseRepository)arr[1];
                MongoRepository _ShareathonRepository = (MongoRepository)arr[2];
                string[] ids = shareathon.socialProfiles.Split(',');
                foreach (string id in ids)
                {

                    Domain.Socioboard.Models.TwitterAccount _TwitterAccount = dbr.Single<Domain.Socioboard.Models.TwitterAccount>(t => t.twitterUserId == id && t.isActive);
                    Domain.Socioboard.Models.User _user = dbr.Single<Domain.Socioboard.Models.User>(t => t.Id == _TwitterAccount.userId);

                    MongoRepository mongoshare = new Helper.MongoRepository("FacebookPageFeedShare");



                    if (_TwitterAccount != null)
                    {
                        var resultshare = mongorepo.Find<FacebookPageFeedShare>(t => t.socialProfiles == shareathon.socialProfiles && t.status == Domain.Socioboard.Enum.ScheduleStatus.Pending);
                        var task = Task.Run(async () =>
                        {
                            return await resultshare;
                        });
                        int count = task.Result.Count;
                        var feedsData = task.Result.ToList();
                        if (count != 0)
                        {
                            foreach (var item in feedsData)
                            {

                                try
                                {
                                    Console.WriteLine(item.socialProfiles + "Scheduling Started");
                                    PostTwitterMessage(item, _TwitterAccount, _user);
                                    Console.WriteLine(item.socialProfiles + "Scheduling");
                                }
                                catch (Exception)
                                {

                                    Thread.Sleep(60000);
                                }
                            }
                            //fbAcc.contenetShareathonUpdate = DateTime.UtcNow;
                            //facebookPage.contenetShareathonUpdate = DateTime.UtcNow;
                            //dbr.Update<Domain.Socioboard.Models.Facebookaccounts>(fbAcc);
                            //dbr.Update<Domain.Socioboard.Models.Facebookaccounts>(facebookPage);
                        }
                        //_TwitterAccount.SchedulerUpdate = DateTime.UtcNow;
                      
                      //  dbr.Update<Domain.Socioboard.Models.TwitterAccount>(_TwitterAccount);

                    }
                }
            }
            catch (Exception)
            {
                Thread.Sleep(60000);
            }
            finally
            {
                noOfthreadRunning--;
                objSemaphore.Release();
                Console.WriteLine(Thread.CurrentThread.Name + " Is Released");
            }
        }

        public static void PostTwitterMessage(Domain.Socioboard.Models.Mongo.FacebookPageFeedShare fbPagefeedshare, Domain.Socioboard.Models.TwitterAccount _TwitterAccount, Domain.Socioboard.Models.User _user)
        {
            try
            {
                DatabaseRepository dbr = new DatabaseRepository();
                MongoRepository _mongofbpostdata = new Helper.MongoRepository("MongoFacebookFeed");
                MongoRepository mongofacebooksharedata = new Helper.MongoRepository("FacebookPageFeedShare");
                var result = _mongofbpostdata.Find<Domain.Socioboard.Models.Mongo.MongoFacebookFeed>(t =>t.ProfileId == fbPagefeedshare.socialProfiles && t.shareStatus==false);
                var task = Task.Run(async () =>
                {
                    return await result;
                });
                IList<Domain.Socioboard.Models.Mongo.MongoFacebookFeed> lstfbpagefeeds = task.Result.ToList();

                foreach(var item in lstfbpagefeeds)
                {
                    if (fbPagefeedshare.scheduleTime <= DateTime.UtcNow)
                    {
                        string twitterdata = ComposeTwitterMessage(item.FeedDescription, item.ProfileId,fbPagefeedshare.userId, item.Picture, false, dbr, _TwitterAccount, _user);
                        if (!string.IsNullOrEmpty(twitterdata) && twitterdata != "feed has not posted")
                        {
                            apiHitsCount++;
                            item.shareStatus = true;
                            item.sharedate = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss");
                            fbPagefeedshare.lastsharestamp = DateTime.UtcNow;


                            FilterDefinition<BsonDocument> filter = new BsonDocument("Id", item.Id);
                            var update = Builders<BsonDocument>.Update.Set("shareStatus", true);

                            _mongofbpostdata.Update<Domain.Socioboard.Models.Mongo.MongoFacebookFeed>(update, filter);
                            mongofacebooksharedata.Update<Domain.Socioboard.Models.Mongo.FacebookPageFeedShare>(update, filter);

                        }
                        else if (twitterdata == "Message not posted")
                        {
                           
                        }
                    }
                }
             
            }
            catch (Exception ex)
            {
                apiHitsCount = MaxapiHitsCount;
            }
        }


        public static string ComposeTwitterMessage(string message, string profileid, long userid, string picurl, bool isScheduled, DatabaseRepository dbr, Domain.Socioboard.Models.TwitterAccount TwitterAccount, Domain.Socioboard.Models.User _user)
        {
            bool rt = false;
            string ret = "";
            string str = "Message posted";
            if (message.Length > 140)
            {
                message = message.Substring(0, 135);
            }

            Domain.Socioboard.Models.TwitterAccount objTwitterAccount = TwitterAccount;
            oAuthTwitter OAuthTwt = new oAuthTwitter(AppSettings.twitterConsumerKey, AppSettings.twitterConsumerScreatKey, AppSettings.twitterRedirectionUrl);
            OAuthTwt.AccessToken = objTwitterAccount.oAuthToken;
            OAuthTwt.AccessTokenSecret = objTwitterAccount.oAuthSecret;
            OAuthTwt.TwitterScreenName = objTwitterAccount.twitterScreenName;
            OAuthTwt.TwitterUserId = objTwitterAccount.twitterUserId;

            Tweet twt = new Tweet();
            if (!string.IsNullOrEmpty(picurl))
            {
                try
                {
                    PhotoUpload ph = new PhotoUpload();
                    string res = string.Empty;
                    rt = ph.NewTweet(picurl, message, OAuthTwt, ref res);
                }
                catch (Exception ex)
                {
                    apiHitsCount = MaxapiHitsCount;
                }
            }
            else
            {
                try
                {
                    JArray post = twt.Post_Statuses_Update(OAuthTwt, message);
                    ret = post[0]["id_str"].ToString();
                }
                catch (Exception ex)
                {
                    apiHitsCount = MaxapiHitsCount;
                }
            }
            if (!string.IsNullOrEmpty(ret) || rt == true)
            {
                string msg = "feed shared successfully";
            }
            else
            {
                string msg = "feed has not posted";
            }

            return str;
        }


    }
}
