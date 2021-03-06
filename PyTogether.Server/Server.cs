﻿using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

using System.Collections.ObjectModel;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

using PyTogether.Network;

namespace PyTogether.Server
{
    class Server
    {
        //--------Constants--------//
        /// <summary>
        /// Default channel for the server. Created on initialization, and all newly connected users are
        /// subscribed to it by default.
        /// </summary>
        private const string DEFAULT_CHANNEL = "Lobby";
        /// <summary>
        /// Name of the file to check for machine-specific import directories
        /// </summary>
        private const string PATHS_FILENAME = "import_paths.cfg";
        /// <summary>
        /// Default port to listen on
        /// </summary>
        private const int PORT = 1357;

        //--------Fields--------//
        private Dictionary<string, ClientInfo> allClients;
        private Dictionary<string, ChannelInfo> channels;

        private Socket listenSocket;

        private ScriptEngine engine;
        //--------Methods--------//
        public Server()
        {
            allClients = new Dictionary<string, ClientInfo>();
            channels = new Dictionary<string, ChannelInfo>();

            engine = Python.CreateEngine();
            initializeSearchPaths();

            channels.Add(DEFAULT_CHANNEL, new ChannelInfo(DEFAULT_CHANNEL, engine, engine.CreateScope()));
        }

        /// <summary>
        /// Main loop of the server, sets up sockets and runs indefinitely
        /// </summary>
        public void Run()
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, PORT);

            listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(localEndPoint);
            listenSocket.Listen(16);

            beginAccept();
            System.Console.WriteLine("Now accepting connections");
            while (true)
            {
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Evaluates all function calls in a message, then sends it to the correct clients
        /// </summary>
        /// <param name="m">Message to route</param>
        public void HandleCompleteData(StreamData data, ClientInfo sender)
        {
            switch (data.GetDataType())
            {
                case Message.DATATYPE: routeMessage(new Message(data.GetFormattedData().ToArray()), sender);
                    break;
                case ChannelRequest.DATATYPE:
                    handleChannelRequest(new ChannelRequest(data.GetFormattedData().ToArray()), sender);
                    break;
            }
        }

        private void routeMessage(Message m, ClientInfo sender)
        {
            if (!channels.ContainsKey(m.ChannelName))
                return;
            if (m.IsInject)
                channels[m.ChannelName].Inject(m.Text);
            else
            {
                m.AddSenderPrefix(sender.Name);
                channels[m.ChannelName].SendToAll(m);
            }
        }
        private void handleChannelRequest(ChannelRequest r, ClientInfo sender)
        {
            bool channelExists = channels.ContainsKey(r.ChannelName);

            if (r.CurrentRequest == ChannelRequest.RequestType.Join && channelExists)
                channels[r.ChannelName].AddClient(sender, r.Password);
            else if (r.CurrentRequest == ChannelRequest.RequestType.Leave && channelExists)
                channels[r.ChannelName].KickClient(sender.Name);
            else if (r.CurrentRequest == ChannelRequest.RequestType.Create && !channelExists)
            {
                channels.Add(r.ChannelName, new ChannelInfo(r.ChannelName, engine, engine.CreateScope(), r.Password));
                System.Console.WriteLine("Channel " + r.ChannelName + " created with password " + r.Password);
            }

        }

        /// <summary>
        /// Calls listenSocket.BeginAccept() with all the correct argumentss. Makes code look cleaner.
        /// </summary>
        private void beginAccept()
        {
            listenSocket.BeginAccept
                (listenSocket.ReceiveBufferSize, new System.AsyncCallback(endAccept), null);
        }
        /// <summary>
        /// Callback for accepting a new connection. Username data must be sent by client as well.
        /// </summary>
        /// <param name="result">IAsyncResult from BeginAccept</param>
        private void endAccept(System.IAsyncResult result)
        {
            byte[] nameData = new byte[listenSocket.ReceiveBufferSize];
            Socket handler = listenSocket.EndAccept(out nameData, result);

            //Create client info and adds them to server lists
            string clientName = System.Text.Encoding.ASCII.GetString(nameData);
            RemoteClientInfo client = new RemoteClientInfo(clientName, HandleCompleteData, KickClient, handler);
            addNewClient(client);

            //Get ready to accept another connection
            beginAccept();
        }

        /// <summary>
        /// Adds new client to the list and subscribes them to DEFAULT_CHANNEL
        /// </summary>
        /// <param name="cl">Client to subscribe</param>
        private void addNewClient(ClientInfo cl)
        {
            allClients.Add(cl.Name, cl);
            channels[DEFAULT_CHANNEL].AddClient(cl, "");
            System.Console.WriteLine("Added client " + cl.Name);
        }
        public void KickClient(string name)
        {
            foreach (ChannelInfo chanInfo in channels.Values)
                chanInfo.KickClient(name);
            allClients.Remove(name);
            System.Console.WriteLine(name + " disconnected");
        }

        /// <summary>
        /// Sets up the engine so it will search the specified directories when looking for imported modules
        /// </summary>
        private void initializeSearchPaths()
        {
            try
            {
                ICollection<string> enginePaths = engine.GetSearchPaths();
                string[] userPaths = System.IO.File.ReadAllLines(PATHS_FILENAME);

                foreach (string p in userPaths)
                {
                    enginePaths.Add(p);
                }
                engine.SetSearchPaths(userPaths);
            }
            catch
            {
                System.Console.WriteLine("Problem opening " + PATHS_FILENAME +
                    " to set default engine search paths. Check that file exists");

            }
        }
    }
}
