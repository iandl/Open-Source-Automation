﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading;
using System.Net;
using OpenSourceAutomation;
using System.Xml;
using System.AddIn;

namespace OSAE.WUnderground
{
    [AddIn("WUnderground", Version = "0.3.5")]
    public class WUnderground : IOpenSourceAutomationAddInv2
    {
        string pName;
        OSAE osae = new OSAE("WUnderground");
        Thread updateConditionsThread, updateForecastThread, updateDayNightThread;
        string pws = "";
        System.Timers.Timer ConditionsUpdateTimer, ForecastUpdateTimer, DayNightUpdateTimer;
        //string feedUrl = "";
        //string ForecastUrl;
        string latitude ="", longitude="";
        int Conditionsupdatetime, Forecastupdatetime, DayNightupdatetime = 60000;
        Boolean FirstRun;
        String DayNight, WeatherObjName;
        Boolean Metric;

        
        public void RunInterface(string pluginName)
        {
            try
            {
                FirstRun = true;
                osae.AddToLog("Running Interface", true);
                pName = pluginName;

                List<OSAEObject> objects = osae.GetObjectsByType("WEATHER");
                if (objects.Count == 0)
                {
                    osae.ObjectAdd("Weather", "Weather Data", "WEATHER", "", "", true);
                    WeatherObjName = "Weather";
                }
                else
                    WeatherObjName = objects[0].Name;


                try
                {
                    if (Boolean.Parse(osae.GetObjectPropertyValue(pName, "Metric").Value))
                    {
                        Metric = true;
                        osae.AddToLog("Using metric units", true);
                    }
                }
                catch
                {
                }
                 

                Conditionsupdatetime = Int32.Parse(osae.GetObjectPropertyValue(pName, "Conditions Interval").Value);
                if (Conditionsupdatetime > 0)
                {
                    ConditionsUpdateTimer = new System.Timers.Timer();
                    ConditionsUpdateTimer.Interval = Conditionsupdatetime * 60000;
                    ConditionsUpdateTimer.Start();
                    ConditionsUpdateTimer.Elapsed += new ElapsedEventHandler(ConditionsUpdateTime);

                    this.updateConditionsThread = new Thread(new ThreadStart(updateconditions));
                    this.updateConditionsThread.Start();

                    Thread.Sleep(5000);
                }
                else
                {
                    latitude = osae.GetObjectPropertyValue(WeatherObjName, "latitude").Value;
                    longitude = osae.GetObjectPropertyValue(WeatherObjName, "longitude").Value;
                }

                Forecastupdatetime = Int32.Parse(osae.GetObjectPropertyValue(pName, "Forecast Interval").Value);
                if (Forecastupdatetime > 0)
                {
                    ForecastUpdateTimer = new System.Timers.Timer();
                    ForecastUpdateTimer.Interval = Forecastupdatetime * 60000;
                    ForecastUpdateTimer.Start();
                    ForecastUpdateTimer.Elapsed += new ElapsedEventHandler(ForecastUpdateTime);

                    this.updateForecastThread = new Thread(new ThreadStart(updateforecast));
                    this.updateForecastThread.Start();             

                }

                DayNightUpdateTimer = new System.Timers.Timer();
                DayNightUpdateTimer.Interval = DayNightupdatetime;
                DayNightUpdateTimer.Start();
                DayNightUpdateTimer.Elapsed += new ElapsedEventHandler(DayNightUpdateTime);

                this.updateDayNightThread = new Thread(new ThreadStart(updateDayNight));
                this.updateDayNightThread.Start();             
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error initializing the plugin " + ex.Message, true);
            }

        
        }

        
        public void ProcessCommand(OSAEMethod method)
        {
            if (method.MethodName == "UPDATE")
                update();
        }


        public void Shutdown()
        {
            osae.AddToLog("Shutting down", true);
            if (Forecastupdatetime > 0)
                ForecastUpdateTimer.Stop();
            if (Conditionsupdatetime > 0)
                ConditionsUpdateTimer.Stop();

            DayNightUpdateTimer.Stop();
        }
       
        public void ConditionsUpdateTime(object sender, EventArgs eArgs)
        {
            if (sender == ConditionsUpdateTimer)
            {
                if (!updateConditionsThread.IsAlive)
                {
                    this.updateConditionsThread = new Thread(new ThreadStart(updateconditions));
                    this.updateConditionsThread.Start();
                }
            }
        }

        public void ForecastUpdateTime(object sender, EventArgs eArgs)
        {
            if (sender == ForecastUpdateTimer)
            {
                if (!updateForecastThread.IsAlive)
                {
                    this.updateForecastThread = new Thread(new ThreadStart(updateforecast));
                    this.updateForecastThread.Start();
                }
            }
        }

        public void DayNightUpdateTime(object sender, EventArgs eArgs)
        {
            if (sender == DayNightUpdateTimer)
            {
                if (!updateDayNightThread.IsAlive)
                {
                    this.updateDayNightThread = new Thread(new ThreadStart(updateDayNight));
                    this.updateDayNightThread.Start();
                }
            }
        }


        private string GetNodeValue(XmlDocument xml, string xPathQuery)
        {
            string nodeValue = string.Empty;
            System.Xml.XmlNode node;
            node = xml.SelectSingleNode(xPathQuery);
            if (node != null)
            {
                nodeValue = node.InnerText;
            }
            return nodeValue;
        }


        private void ReportFieldValue(string fieldName, string fieldValue)
        {
            if (fieldValue.Length > 0)
            {
                osae.ObjectPropertySet(WeatherObjName, fieldName, fieldValue);
                if (fieldName == "Temp")
                {
                    osae.AddToLog("Found " + fieldName + ": " + fieldValue, true);
                }
                else
                {
                    osae.AddToLog("Found " + fieldName + ": " + fieldValue, false);
                    //System.Diagnostics.Debug.WriteLine("Found " + fieldName + ": " + fieldValue);
                }
            }
            else
            {
                if (fieldName != "Windchill" & fieldName != "Visibility" & fieldName != "Conditions")
                {
                    osae.AddToLog("NOT FOUND " + fieldName, true);
                }

            }
        }


        private void GetFieldFromXmlAndReport(XmlDocument xml, string fieldName, string xPathQuery)
        {
            string fieldValue = GetNodeValue(xml, xPathQuery);
            ReportFieldValue(fieldName, fieldValue);
        }

        public void update()
        {
            if (Conditionsupdatetime > 0)
                updateconditions();
            if (Forecastupdatetime > 0)
                updateforecast();
        }

        public void updateconditions()
        {
            string feedUrl;
            string sXml;
            WebClient webClient = new WebClient();
            XmlDocument xml;

            try
            {
                pws = osae.GetObjectPropertyValue(pName, "PWS").Value;
                if (pws != "")
                {
                    feedUrl = "http://api.wunderground.com/weatherstation/WXCurrentObXML.asp?ID=" + pws;
                    sXml = webClient.DownloadString(feedUrl);
                    xml = new XmlDocument();
                    xml.LoadXml(sXml);

                    //update all the weather variables

                    #region Current Observation
                    // Seems to be returned from both pws and airport.
                    GetFieldFromXmlAndReport(xml, "Wind Speed", "current_observation/wind_mph");
                    GetFieldFromXmlAndReport(xml, "Wind Directions", "current_observation/wind_dir");
                    GetFieldFromXmlAndReport(xml, "Humidity", "current_observation/relative_humidity");
                    GetFieldFromXmlAndReport(xml, "Image", "current_observation/image/url");
                    GetFieldFromXmlAndReport(xml, "Last Updated", "current_observation/observation_time");
                    
                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Temp", "current_observation/temp_f");
                        GetFieldFromXmlAndReport(xml, "Pressure", "current_observation/pressure_in");
                        GetFieldFromXmlAndReport(xml, "Dewpoint", "current_observation/dewpoint_f");
                        GetFieldFromXmlAndReport(xml, "Windchill", "current_observation/windchill_f");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Temp", "current_observation/temp_c"); 
                        GetFieldFromXmlAndReport(xml, "Pressure", "current_observation/pressure_mb");
                        GetFieldFromXmlAndReport(xml, "Dewpoint", "current_observation/dewpoint_c");
                        GetFieldFromXmlAndReport(xml, "Windchill", "current_observation/windchill_c");
                    }
                    
                    // Only returned for airports.
                    GetFieldFromXmlAndReport(xml, "Visibility", "current_observation/visibility_mi");
                    GetFieldFromXmlAndReport(xml, "Conditions", "current_observation/weather");

                    if (FirstRun)
                    {
                        latitude = GetNodeValue(xml,"current_observation/location/latitude");
                        longitude = GetNodeValue(xml, "current_observation/location/longitude");
                        osae.ObjectPropertySet(WeatherObjName, "latitude", latitude);
                        osae.ObjectPropertySet(WeatherObjName, "logitude", longitude);
                        FirstRun = false;
                    }
                    //ForecastUrl = GetNodeValue(xml, "current_observation/ob_url");

                    #endregion
                }                                    
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error updating current weather - " + ex.Message, true);
            }
        }


        public void updateforecast()
        {
            string feedUrl;
            string sXml;
            WebClient webClient = new WebClient();
            XmlDocument xml;

            try
            {
                if (latitude != "" && longitude != "")
                {
                    osae.AddToLog("Update Forecast", true);
                   // Now get the forecast.
                    feedUrl = "http://api.wunderground.com/auto/wui/geo/ForecastXML/index.xml?query=" + latitude + "," + longitude;
                    sXml = webClient.DownloadString(feedUrl);
                    xml = new XmlDocument();
                    xml.LoadXml(sXml);

                    ReportFieldValue("Sunrise", GetNodeValue(xml, "forecast/moon_phase/sunrise/hour") + ":" + GetNodeValue(xml, "forecast/moon_phase/sunrise/minute"));
                    ReportFieldValue("Sunset", GetNodeValue(xml, "forecast/moon_phase/sunset/hour") + ":" + GetNodeValue(xml, "forecast/moon_phase/sunset/minute"));

                    // NOTE:
                    // Need to grab a few different forecasts at various times of day to see how wunderground
                    // manages the daynumber.  Can't find any specification that lays it out clearly.
                    // This is the closest to a specification found:
                    // http://wiki.wunderground.com/index.php/API_-_XML
                    // 


                    GetFieldFromXmlAndReport(xml, "Today Precip", @"forecast/simpleforecast/forecastday[period=1]/pop");
                    GetFieldFromXmlAndReport(xml, "Today Forecast", @"forecast/simpleforecast/forecastday[period=1]/conditions");
                    GetFieldFromXmlAndReport(xml, "Today Image", @"forecast/simpleforecast/forecastday[period=1]/icons/icon_set[@name='Contemporary']/icon_url");
                    GetFieldFromXmlAndReport(xml, "Today Summary", @"forecast/txt_forecast/forecastday[period=1]/fcttext");

                    //GetFieldFromXmlAndReport(xml, "Tonight Precip", @"forecast/");
                    //GetFieldFromXmlAndReport(xml, "Tonight Forecast", @"forecast/");
                    //GetFieldFromXmlAndReport(xml, "Tonight Image", @"forecast/");
                    GetFieldFromXmlAndReport(xml, "Tonight Summary", @"forecast/txt_forecast/forecastday[period=1]/fcttext");


                    #region Period1
                    GetFieldFromXmlAndReport(xml, "Day1 Precip", @"forecast/simpleforecast/forecastday[period=2]/pop");
                    GetFieldFromXmlAndReport(xml, "Day1 Forecast", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day1 Summary", @"forecast/txt_forecast/forecastday[period=2]/fcttext");
                    GetFieldFromXmlAndReport(xml, "Day1 Label", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day1 Image", @"forecast/simpleforecast/forecastday[period=2]/icons/icon_set[@name='Contemporary']/icon_url");

                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Day1 High", @"forecast/simpleforecast/forecastday[period=2]/high/fahrenheit");
                        GetFieldFromXmlAndReport(xml, "Night1 Low", @"forecast/simpleforecast/forecastday[period=2]/low/fahrenheit");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Day1 High", @"forecast/simpleforecast/forecastday[period=2]/high/celsius");
                        GetFieldFromXmlAndReport(xml, "Night1 Low", @"forecast/simpleforecast/forecastday[period=2]/low/celsius");
                    }
                    

                    //GetFieldFromXmlAndReport(xml, "Night1 Precip", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night1 Forecast", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night1 Summary", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night1 Label", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night1 Image", @"forecast/simpleforecast/forecastday[period=2]/");
                    #endregion

                    #region Period2
                    GetFieldFromXmlAndReport(xml, "Day2 Precip", @"forecast/simpleforecast/forecastday[period=3]/pop");
                    GetFieldFromXmlAndReport(xml, "Day2 Forecast", @"forecast/simpleforecast/forecastday[period=3]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day2 Summary", @"forecast/txt_forecast/forecastday[period=3]/fcttext");
                    GetFieldFromXmlAndReport(xml, "Day2 Label", @"forecast/simpleforecast/forecastday[period=3]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day2 Image", @"forecast/simpleforecast/forecastday[period=3]/icons/icon_set[@name='Contemporary']/icon_url");

                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Day2 High", @"forecast/simpleforecast/forecastday[period=3]/high/fahrenheit");
                        GetFieldFromXmlAndReport(xml, "Night2 Low", @"forecast/simpleforecast/forecastday[period=3]/low/fahrenheit");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Day2 High", @"forecast/simpleforecast/forecastday[period=3]/high/celsius");
                        GetFieldFromXmlAndReport(xml, "Night2 Low", @"forecast/simpleforecast/forecastday[period=3]/low/celsius");
                    }

                    //GetFieldFromXmlAndReport(xml, "Night2 Precip", @"forecast/simpleforecast/forecastday[period=3]/");
                    //GetFieldFromXmlAndReport(xml, "Night2 Forecast", @"forecast/simpleforecast/forecastday[period=3]/");
                    //GetFieldFromXmlAndReport(xml, "Night2 Summary", @"forecast/simpleforecast/forecastday[period=3]/");
                    //GetFieldFromXmlAndReport(xml, "Night2 Label", @"forecast/simpleforecast/forecastday[period=3]/");
                    //GetFieldFromXmlAndReport(xml, "Night2 Image", @"forecast/simpleforecast/forecastday[period=3]/");
                    #endregion

                    #region Period3
                    GetFieldFromXmlAndReport(xml, "Day3 Precip", @"forecast/simpleforecast/forecastday[period=4]/pop");
                    GetFieldFromXmlAndReport(xml, "Day3 Forecast", @"forecast/simpleforecast/forecastday[period=4]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day3 Summary", @"forecast/txt_forecast/forecastday[period=4]/fcttext");
                    GetFieldFromXmlAndReport(xml, "Day3 Label", @"forecast/simpleforecast/forecastday[period=4]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day3 Image", @"forecast/simpleforecast/forecastday[period=4]/icons/icon_set[@name='Contemporary']/icon_url");

                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Day3 High", @"forecast/simpleforecast/forecastday[period=4]/high/fahrenheit");
                        GetFieldFromXmlAndReport(xml, "Night3 Low", @"forecast/simpleforecast/forecastday[period=4]/low/fahrenheit");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Day3 High", @"forecast/simpleforecast/forecastday[period=4]/high/celsius");
                        GetFieldFromXmlAndReport(xml, "Night3 Low", @"forecast/simpleforecast/forecastday[period=4]/low/celsius");
                    }


                    //GetFieldFromXmlAndReport(xml, "Night3 Precip", @"forecast/simpleforecast/forecastday[period=4]/");
                    //GetFieldFromXmlAndReport(xml, "Night3 Forecast", @"forecast/simpleforecast/forecastday[period=4]/");
                    //GetFieldFromXmlAndReport(xml, "Night3 Summary", @"forecast/simpleforecast/forecastday[period=4]/");
                    //GetFieldFromXmlAndReport(xml, "Night3 Label", @"forecast/simpleforecast/forecastday[period=4]/");
                    //GetFieldFromXmlAndReport(xml, "Night3 Image", @"forecast/simpleforecast/forecastday[period=4]/");
                    #endregion

                    #region Period4
                    GetFieldFromXmlAndReport(xml, "Day4 Precip", @"forecast/simpleforecast/forecastday[period=5]/pop");
                    GetFieldFromXmlAndReport(xml, "Day4 Forecast", @"forecast/simpleforecast/forecastday[period=5]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day4 Summary", @"forecast/txt_forecast/forecastday[period=5]/fcttext");
                    GetFieldFromXmlAndReport(xml, "Day4 Label", @"forecast/simpleforecast/forecastday[period=5]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day4 Image", @"forecast/simpleforecast/forecastday[period=5]/icons/icon_set[@name='Contemporary']/icon_url");

                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Day4 High", @"forecast/simpleforecast/forecastday[period=5]/high/fahrenheit");
                        GetFieldFromXmlAndReport(xml, "Night4 Low", @"forecast/simpleforecast/forecastday[period=5]/low/fahrenheit");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Day4 High", @"forecast/simpleforecast/forecastday[period=5]/high/celsius");
                        GetFieldFromXmlAndReport(xml, "Night4 Low", @"forecast/simpleforecast/forecastday[period=5]/low/celsius");
                    }


                    //GetFieldFromXmlAndReport(xml, "Night4 Precip", @"forecast/simpleforecast/forecastday[period=5]/");
                    //GetFieldFromXmlAndReport(xml, "Night4 Forecast", @"forecast/simpleforecast/forecastday[period=5]/");
                    //GetFieldFromXmlAndReport(xml, "Night4 Summary", @"forecast/simpleforecast/forecastday[period=5]/");
                    //GetFieldFromXmlAndReport(xml, "Night4 Label", @"forecast/simpleforecast/forecastday[period=5]/");
                    //GetFieldFromXmlAndReport(xml, "Night4 Image", @"forecast/simpleforecast/forecastday[period=5]/");
                    #endregion

                    #region Period5
                    GetFieldFromXmlAndReport(xml, "Day5 Precip", @"forecast/simpleforecast/forecastday[period=6]/pop");
                    GetFieldFromXmlAndReport(xml, "Day5 Forecast", @"forecast/simpleforecast/forecastday[period=6]/conditions");
                    //GetFieldFromXmlAndReport(xml, "Day5 Summary", @"forecast/txt_forecast/forecastday[period=6]/fcttext");
                    GetFieldFromXmlAndReport(xml, "Day5 Label", @"forecast/simpleforecast/forecastday[period=6]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day5 Image", @"forecast/simpleforecast/forecastday[period=6]/icons/icon_set[@name='Contemporary']/icon_url");

                    if (!Metric)
                    {
                        GetFieldFromXmlAndReport(xml, "Day5 High", @"forecast/simpleforecast/forecastday[period=6]/high/fahrenheit");
                        GetFieldFromXmlAndReport(xml, "Nigh5 Low", @"forecast/simpleforecast/forecastday[period=6]/low/fahrenheit");
                    }
                    else
                    {
                        GetFieldFromXmlAndReport(xml, "Day5 High", @"forecast/simpleforecast/forecastday[period=6]/high/celsius");
                        GetFieldFromXmlAndReport(xml, "Night5 Low", @"forecast/simpleforecast/forecastday[period=6]/low/celsius");
                    }

                    //GetFieldFromXmlAndReport(xml, "Night5 Precip", @"forecast/simpleforecast/forecastday[period=6]/");
                    //GetFieldFromXmlAndReport(xml, "Night5 Forecast", @"forecast/simpleforecast/forecastday[period=6]/");
                    //GetFieldFromXmlAndReport(xml, "Night5 Summary", @"forecast/simpleforecast/forecastday[period=6]/");
                    //GetFieldFromXmlAndReport(xml, "Night5 Label", @"forecast/simpleforecast/forecastday[period=6]/");
                    //GetFieldFromXmlAndReport(xml, "Night5 Image", @"forecast/simpleforecast/forecastday[period=6]/");
                    #endregion

                    #region Period6
                    /*  WUNDERGROUND doesn't go this far.
                    GetFieldFromXmlAndReport(xml, "Day6 High", @"forecast/simpleforecast/forecastday[period=2]/high/fahrenheit");
                    //GetFieldFromXmlAndReport(xml, "Day6 Precip", @"forecast/simpleforecast/forecastday[period=2]/");
                    GetFieldFromXmlAndReport(xml, "Day6 Forecast", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day6 Summary", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day6 Label", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day6 Image", @"forecast/simpleforecast/forecastday[period=2]/icons/icon_set[@name='Contemporary']/icon_url");

                    GetFieldFromXmlAndReport(xml, "Night6 Low", @"forecast/simpleforecast/forecastday[period=2]/low/fahrenheit");
                    //GetFieldFromXmlAndReport(xml, "Night6 Precip", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night6 Forecast", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night6 Summary", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night6 Label", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night6 Image", @"forecast/simpleforecast/forecastday[period=2]/");
                     * */
                    #endregion

                    #region Period7
                    /*  WUNDERGROUND doesn't go this far.
                    GetFieldFromXmlAndReport(xml, "Day7 High", @"forecast/simpleforecast/forecastday[period=2]/high/fahrenheit");
                    //GetFieldFromXmlAndReport(xml, "Day7 Precip", @"forecast/simpleforecast/forecastday[period=2]/");
                    GetFieldFromXmlAndReport(xml, "Day7 Forecast", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day7 Summary", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day7 Label", @"forecast/simpleforecast/forecastday[period=2]/conditions");
                    GetFieldFromXmlAndReport(xml, "Day7 Image", @"forecast/simpleforecast/forecastday[period=2]/icons/icon_set[@name='Contemporary']/icon_url");

                    GetFieldFromXmlAndReport(xml, "Night7 Low", @"forecast/simpleforecast/forecastday[period=2]/low/fahrenheit");
                    //GetFieldFromXmlAndReport(xml, "Night7 Precip", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night7 Forecast", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night7 Summary", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night7 Label", @"forecast/simpleforecast/forecastday[period=2]/");
                    //GetFieldFromXmlAndReport(xml, "Night7 Image", @"forecast/simpleforecast/forecastday[period=2]/");
                     * */
                    #endregion
                }
                
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error updating forecasted weather - " + ex.Message, true);
            }
        }
        public void updateDayNight()
        {
            TimeSpan Now;
            TimeSpan DuskStart;
            TimeSpan DuskEnd;
            TimeSpan DawnStart;
            TimeSpan DawnEnd;
            TimeSpan Sunrise;
            TimeSpan Sunset;
            String DawnPreString;
            String DawnPostString;
            String DuskPreString;
            String DuskPostString;
            Int32 DuskPre;
            Int32 DuskPost;
            Int32 DawnPre;
            Int32 DawnPost;
            Int32 Number;
            

            try
            {
                Now = DateTime.Now.TimeOfDay;
                Sunrise = DateTime.Parse(osae.GetObjectPropertyValue(WeatherObjName, "Sunrise").Value).TimeOfDay;
                Sunset = DateTime.Parse(osae.GetObjectPropertyValue(WeatherObjName, "Sunset").Value).TimeOfDay;

                DawnPreString= osae.GetObjectPropertyValue(pName, "DawnPre").Value;
                if (Int32.TryParse(DawnPreString, out Number))
                {
                    DawnPre = Number;                   
                }
                else
                {
                    DawnPre = 0;
                }


                DawnPostString = osae.GetObjectPropertyValue(pName, "DawnPost").Value;
                if (Int32.TryParse(DawnPostString, out Number))
                {
                    DawnPost = Number;
                }
                else
                {
                    DawnPost = 0;
                }


                DuskPreString = osae.GetObjectPropertyValue(pName, "DuskPre").Value;
                if (Int32.TryParse(DuskPreString, out Number))
                {
                    DuskPre = Number;
                }
                else
                {
                    DuskPre = 0;
                }


                DuskPostString = osae.GetObjectPropertyValue(pName, "DuskPost").Value;
                if (Int32.TryParse(DuskPostString, out Number))
                {
                    DuskPost = Number;
                }
                else
                {
                    DuskPost = 0;
                }

                DawnStart = Sunrise - TimeSpan.FromMinutes(DawnPre);
                DawnEnd = Sunrise + TimeSpan.FromMinutes(DawnPost);
                
                DuskStart =  Sunset - TimeSpan.FromMinutes(DuskPre);
                DuskEnd = Sunset + TimeSpan.FromMinutes(DuskPost);

                String John = " " + " " + Convert.ToString(DuskStart);

                osae.AddToLog(Convert.ToString(DawnStart) + " " + Convert.ToString(DawnEnd) + " " + Convert.ToString(DuskStart) + " " + Convert.ToString(DuskEnd) + " ", false);
               
                if (Now >= DawnEnd & Now < DuskStart)
                {
                    if (DayNight != "Day")
                    {
                        osae.ObjectPropertySet(WeatherObjName, "DayNight", "Day");
                        if (DayNight == "Night" | DayNight == "Dawn")
                        {
                            osae.EventLogAdd(WeatherObjName, "Day");
                        }
                        DayNight = "Day";
                        osae.AddToLog("Day", false);
                        
                    }
                }
                else if (Now >= DuskEnd | Now < DawnStart)
                {
                    if (DayNight != "Night")
                    {
                        osae.ObjectPropertySet(WeatherObjName, "DayNight", "Night");
                        if (DayNight == "Day" | DayNight == "Dusk")
                        {
                            osae.EventLogAdd(WeatherObjName, "Night");
                        }
                        DayNight = "Night";
                         osae.AddToLog("Night", true);
                    }
                }
                else if (Now >= DawnStart & Now < DawnEnd)
                {
                    if (DayNight != "Dawn")
                    {
                        osae.ObjectPropertySet(WeatherObjName, "DayNight", "Dawn");
                        if (DayNight == "Night")
                        {
                            osae.EventLogAdd(WeatherObjName, "Dawn");
                        }
                        DayNight = "Dawn";
                        osae.AddToLog("Dawn", true);

                    }

                }

                else if (Now >= DuskStart & Now < DuskEnd)
                {
                    if (DayNight != "Dusk")
                    {
                        osae.ObjectPropertySet(WeatherObjName, "DayNight", "Dusk");
                        if (DayNight == "Day")
                        {
                            osae.EventLogAdd(WeatherObjName, "Dusk");
                        }
                        DayNight = "Dusk";
                        osae.AddToLog("Dusk", true);
                    }

                }

            }
            catch (Exception ex)
            {
                osae.AddToLog("Error updating day/night " + ex.Message, true);
            }

        }
    }
}