using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IDG;
using IDG.FightServer;
namespace IDG_FightServer
{
    class Program
    {
       
        static void Main(string[] args)
        {
            Console.WriteLine("请输入监听的IP");
            //string ip = Console.ReadLine();
            string ip = "127.0.0.1";
            var serverManager = new FightServerManager();
            serverManager.Start(ip + ":44444");
            while (true)
            {
                Console.ReadLine();
            }

        }
        
    }
}
