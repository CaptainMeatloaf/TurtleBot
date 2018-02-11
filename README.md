TurtleBot
=========

This is the source for TurtleBot, used on the TurtleCoin discord server

## Setting Up

This bot uses .NET Core 2.0. To install, use the following link: https://www.microsoft.com/net/learn/get-started/ and select the instructions relevant to your OS.

For an IDE I recommend Visual Studio Code.

## Usage

Get a bot token by making an app (instructions [here](https://discord.foxbot.me/docs/guides/getting_started/intro.html))

Create a file called `config.json` with the following contents
```
{
  "token": "<your token here>",
  "tags": {
    "use": {
      "approvedRoles": [ <approved role IDs for use here> ],
      "approvedUsers": [ <approved user IDs for use here> ],
      "permittedChannels": [ <channel IDs tags are allowed in here> ]
    },
    "edit": {
      "approvedRoles": [ <approved role IDs for edit here> ],
      "approvedUsers": [ <approved user IDs for edit here> ]
    }
  },
  "database": {
    "connectionString": "Filename=TurtleBot.db" //Or any other databse name/AQLite conenction string you desire
  } 
}
```

**DO NOT SHARE YOUR TOKEN WITH ANYBODY**

Further instructions vary by your IDE. Project should open up straight away and bre ready to go in VS Code.
