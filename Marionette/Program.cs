using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;

namespace Marionette
{
    class Program
    {
        static void Main(string[] args)
        {
            IWebDriver driver = new MarionetteDriver();
            driver.Url = "http://www.mozilla.org/en-US/";
            IWebElement wrapper = driver.FindElement(By.Id("wrapper"));
            IWebElement element = wrapper.FindElement(By.TagName("h2"));
            string text = element.Text;
            driver.Quit();
            Console.WriteLine(text);
            Console.ReadLine();
        }
    }
}
