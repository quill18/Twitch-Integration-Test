using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

// References:
//   Twitch Dev Documentation:
//              https://dev.twitch.tv/docs/irc/
//
//   This existing chatbot project was used as a template for this tutorial:
//              https://github.com/allison-liem/unity-twitch
//     ( specifically, it implements the example JS parser as C#:  https://dev.twitch.tv/docs/irc/example-parser/ )

namespace QuillsAwesomeChat
{
    public class ConnectionManager : MonoBehaviour
    {
        // This is the id of the registered Twitch Application.  This should be changed to
        // match the application you registered on Twitch.
        //   https://dev.twitch.tv/console/apps/create
        // I'm not sharing mine because if you're lazy and use mine, your app will break
        // if I delete the registration on Twitch!
        private string clientId = "I BELIEVE THIS IS SAFE TO SHARE PUBLICLY";

        // The accessToken is how this application will actually log in as a Twitch user.
        // This is SECRET and should not be shared. Note that this can be extracted from
        // your compiled executable, so don't even share your .EXE if you set the token
        // here (as opposed to saving/loading it from PlayerPrefs at run time).
        private string accessToken = "SECRET VALUE DO NOT SHARE!!!";

        // Get the SECRET AccessToken by going to this link while logged in as the account you want this bot to act as:
        //   https://id.twitch.tv/oauth2/authorize?response_type=token&client_id=CLIENT_ID_GOES_HERE&redirect_uri=https://id.twitch.tv/oauth2/authorize&scope=chat%3Aread+chat%3Aedit
        //                                                                       ^^^^^^^^^^^^^^^^^^^
        //
        // Because the redirect_uri is set to a dummy endpoint, you'll get an error message -- but the accessToken will be in the address you are redirected to:
        //
        //          https://id.twitch.tv/oauth2/authorize#access_token=SECRET_ACCESS_TOKEN_HERE&scope=[...]
        //                                                             ^^^^^^^^^^^^^^^^^^^^^^^^
        //
        // As-is, you'll have to do this manually in a browser.
        //
        // Information on how to get the auth token is available here:
        //     https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/
        // Note that we use Implicit Grant Flow because we don't have a server that can maintain a Client Secret in a private manner.
        // You could make a very basic server that acts as an in-between the Unity App and the Twitch authentication, in which case you
        // might prefer something like Client Credentials Grant Flow instead.
        // Alternatively, if you are making this application just for yourself you could include the secrets needed in code here,
        // but make sure to never share your code nor your compiled executable with anyone else.

        // The username your bot will be operating as (which matches the account used to create the secret accessToken)
        private const string username = "quill18";

        // The channel your bot will join
        private const string channel = "quill18";

        // Objects to manage our network connections:
        private TcpClient tcpClient;
        private StreamReader reader;
        private StreamWriter writer;

        // Making everything private to start, but I suspect connectionState will change to a public getter
        private enum ConnectionState { Disconnected, Disconnecting, Connected, Connecting }
        private ConnectionState connectionState = ConnectionState.Disconnected;

        // Twitch has various rate limits depending on the type of account operating in chat
        // Here we make sure we don't post more than X lines in Y seconds
        // (not relevant if doing a read-only bot)
        private int numLinesPerInterval = 15;
        private float interval = 30;
        private int numLinesSent;
        private float intervalRemaining;
        private Queue<string> linesToSend;

        // Should we act as a Unity-style "singleton" with DontDestroyOnLoad?
        // This is important if there are scene-changes, unless we want to 
        // re-connect to Twitch in every scene.
        private bool actAsSingleton = false;
        private static ConnectionManager instance = null;

        public delegate void ChatMessageListener(string source, string parameters);
        public event ChatMessageListener ChatMessageListeners;

        // Start is called before the first frame update
        void Start()
        {
            // How to store the access token in the player prefs:
            //PlayerPrefs.SetString("QAC_TwitchClientId", "");
            //PlayerPrefs.SetString("QAC_TwitchAccessToken", "");

            // I have previously stored my secret access token in my computer's playerprefs,
            // so I just retrieve it here (so that I don't have to have it in my code, which is going on github)
            accessToken = PlayerPrefs.GetString("QAC_TwitchAccessToken");

            // Again, I believe application client id is public, but I'm avoiding hardcoding my
            // own here to make sure you create your own unique client id on the Twitch dev site!
            clientId = PlayerPrefs.GetString("QAC_TwitchClientId");

            if (actAsSingleton)
            {
                // THERE CAN ONLY BE ONE
                if (instance != null)
                {
                    // We already exist.
                    Destroy(gameObject);
                    return;
                }

                instance = this;
                DontDestroyOnLoad(gameObject);
            }

            Initialize();
            Connect();
        }

        private void Update()
        {
            // TODO:  Check that connection is still alive

            SendQueuedMessages();
        }

        private void OnDestroy()
        {
            // Make sure to kill network connection when this object is destroyed,
            // including when we stop the application -- very important for running in the editor
            instance = null;
            Disconnect();
        }

        void Initialize()
        {
            linesToSend = new Queue<string>();
        }

        void Connect()
        {
            try
            {
                connectionState = ConnectionState.Connecting;

                tcpClient = new TcpClient("irc.twitch.tv", 6667);
                reader = new StreamReader(tcpClient.GetStream());
                writer = new StreamWriter(tcpClient.GetStream());
                writer.WriteLine("PASS oauth:" + accessToken);
                writer.WriteLine("NICK " + username);   // Note: It's possible username/channel needs to be force ToLower()
                // writer.WriteLine("CAP REQ twitch.tv/tags"); // Request message tags, like color -- and also emotes used?
                writer.WriteLine("JOIN #" + channel);
                writer.Flush();

                numLinesSent = 3;
                intervalRemaining = interval;

                connectionState = ConnectionState.Connected;

                StartCoroutine( ReadStream() );
            }
            catch (System.Exception e)
            {
                Disconnect();
                Debug.Log("FAILED TO CONNECT: " + e);
            }
        }

        void Disconnect()
        {
            tcpClient?.Close();
            connectionState = ConnectionState.Disconnected;
        }

        private IEnumerator ReadStream()
        {
            while (connectionState == ConnectionState.Connected)
            {
                if (tcpClient.Available > 0 || reader.Peek() > 0)
                {
                    string line = reader.ReadLine();
                    //Debug.Log(line);
                    ProcessLine(line);
                }

                yield return null;
            }
        }

        // A Twitch line of text looks like this:
        // :username_of_chatter!foo@foo.tmi.twitch.tv PRIVMSG #quill18 :Quill is the best and/or worst!

        private void ProcessLine(string line)
        {
            // parse the message into sub-parts
            var (command, source, parameters) = ParseMessage(line);

            // do something based on the "command"
            if (command == "PING")
            {
                // Send keepalive response
                SendLine("PONG " + parameters, true );
            }
            else if (command == "PRIVMSG")
            {
                // This is a regular line of twich chat
                ChatMessageListeners?.Invoke(source, parameters);
            }
            else
            {
                // TODO:  Handle authentication failure
                //Debug.LogWarning("Unrecognized chat command: " + command);
            }
        }

        private void SendLine(string line, bool sendNow = false )
        {
            if (sendNow)
            {
                writer.WriteLine(line);
                writer.Flush();
                numLinesSent++;
            }
            else
            {
                linesToSend.Enqueue(line);
            }
        }

        private void SendQueuedMessages()
        {
            // This is called every Update
            intervalRemaining -= Time.deltaTime;

            if (intervalRemaining < 0)
            {
                // We can reset our anti-spam interval
                numLinesSent = 0;
                intervalRemaining = interval;
            }

            bool didWriteLines = false;

            while (numLinesSent < numLinesPerInterval && linesToSend.Count > 0)
            {
                writer.WriteLine(linesToSend.Dequeue());
                numLinesSent++;
                didWriteLines = true;
            }

            if (didWriteLines)
            {
                writer.Flush();
            }

        }






        // EVERYTHING BELOW THIS WAS COPIED FROM: https://github.com/allison-liem/unity-twitch
        private (string, string, string) ParseMessage(string message)
        {
            // Converted to C# and modified from: https://dev.twitch.tv/docs/irc/example-parser

            string command;
            string source = null;
            string parameters = null;

            // The start index. Increments as we parse the IRC message
            int idx = 0;

            // The raw components of the IRC message
            //string rawTagsComponent = null;
            string rawCommandComponent;
            string rawSourceComponent = null;
            string rawParametersComponent = null;

            int endIdx;

            // If the message includes tags, get the tags component of the IRC message
            if (message[idx] == '@')
            {
                endIdx = message.IndexOf(' ');
                // We ignore the tags
                //rawTagsComponent = message.Substring(1, endIdx - 1);
                idx = endIdx + 1; // Should now point to the source colon (:).
            }

            // Get the source component (nick and host) of the IRC message.
            // The idx should point to the source part; otherwise, it's a PING command.
            if (message[idx] == ':')
            {
                idx += 1;
                endIdx = message.IndexOf(' ', idx);
                rawSourceComponent = message.Substring(idx, endIdx - idx);
                idx = endIdx + 1; // Should point to the command part of the message.
            }

            // Get the command component of the IRC message
            endIdx = message.IndexOf(':', idx); // Looking for the parameters parts of the message.
            if (endIdx == -1)                   // But not all messages include the parameters part.
            {
                endIdx = message.Length;
            }
            rawCommandComponent = message.Substring(idx, endIdx - idx).Trim();

            // Get the parameters component of the IRC message.
            if (endIdx != message.Length) // Check if the IRC message contains a parameters component.
            {
                idx = endIdx + 1;         // Should point to the parameters part of the message.
                rawParametersComponent = message.Substring(idx);
            }

            command = ParseCommand(rawCommandComponent);
            if (command != null)
            {
                source = ParseSource(rawSourceComponent);
                // We don't parse the raw parameters further
                parameters = rawParametersComponent?.Trim();
            }

            return (command, source, parameters);

        }

        private string ParseCommand(string rawCommandComponent)
        {
            if (string.IsNullOrEmpty(rawCommandComponent))
            {
                return null;
            }

            var commandParts = rawCommandComponent.Split(' ');
            return commandParts[0];
        }

        private string ParseSource(string rawSourceComponent)
        {
            if (string.IsNullOrEmpty(rawSourceComponent))
            {
                return null;
            }
            string[] sourceParts = rawSourceComponent.Split('!');
            if (sourceParts.Length == 2)
            {
                return sourceParts[0];
            }
            else
            {
                return null;
            }
        }

    }
}