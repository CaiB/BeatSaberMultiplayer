using System;
using Newtonsoft.Json.Linq;

namespace BeatSaberMultiplayer.Misc
{
    public enum SongQueueState { Queued, Downloading, Downloaded, Error };
    
    [Serializable]
    public class Song
    {
        public string levelAuthorName;
        public string songAuthorName;
        public string songName;
        public string songSubName;
        public float bpm;
        public float duration;
        public int downloads;
        public int plays;
        public int upVotes;
        public int downVotes;
        public float rating;
        public float heat;
        public string description;
        public string _id;
        public string key;
        public string name;
        public string ownerid;
        public string ownerName;
        public string hash;
        public string uploaded;
        public string downloadURL;
        public string coverURL;
        public string img;


        public string path;
        public bool scoreSaber;

        public SongQueueState songQueueState = SongQueueState.Queued;

        public float downloadingProgress = 0f;

        public Song()
        {

        }

        public Song(JObject jsonNode, bool scoreSaber)
        {
            if (scoreSaber)
            {
                this.scoreSaber = scoreSaber;
                ConstructFromScoreSaber(jsonNode);
                return;
            }
            levelAuthorName = (string)jsonNode["metadata"]["levelAuthorName"];
            songAuthorName = (string)jsonNode["metadata"]["songAuthorName"];
            songName = (string)jsonNode["metadata"]["songName"];
            songSubName = (string)jsonNode["metadata"]["songSubName"];
            bpm = (float)jsonNode["metadata"]["bpm"];
            duration = (float)jsonNode["metadata"]["duration"];
            downloads = 0;
            plays = 0; // These 2 were removed from the API
            upVotes = (int)jsonNode["stats"]["upvotes"];
            downVotes = (int)jsonNode["stats"]["downvotes"];
            rating = (float)jsonNode["stats"]["score"];
            heat = 0.0F; // TODO: Is this still used?
            description = (string)jsonNode["description"];
            _id = (string)jsonNode["id"];
            key = (string)jsonNode["id"]; // TODO: Are these 2 correct? Seems to work...
            name = (string)jsonNode["name"];
            ownerid = (string)jsonNode["uploader"]["id"];
            ownerName = (string)jsonNode["uploader"]["name"];
            hash = (string)(jsonNode["versions"][0]["hash"]);
            hash = hash.ToLower();
            uploaded = (string)jsonNode["uploaded"];
            downloadURL = (string)(jsonNode["versions"][0]["downloadURL"]);
            coverURL = (string)(jsonNode["versions"][0]["coverURL"]);
        }

        public void ConstructFromScoreSaber(JObject jsonNode)
        {
            _id = "";
            ownerid = "";
            downloads = 0;
            upVotes = 0;
            downVotes = 0;
            plays = 0;
            description = "";
            uploaded = "";
            rating = 0;
            heat = 0f;
            key = "";
            name = "";
            ownerName = "";
            downloadURL = "";
            songName = (string)jsonNode["name"];
            songSubName = (string)jsonNode["songSubName"];
            levelAuthorName = (string)jsonNode["levelAuthorName"];
            songAuthorName = (string)jsonNode["songAuthorName"];
            bpm = (int)jsonNode["bpm"];
            coverURL = (string)jsonNode["image"];
            hash = (string)jsonNode["id"];
            hash = hash.ToLower();
        }

        public static Song FromSearchNode(JObject jsonNode)
        {
            Song buffer = new Song();
            buffer.levelAuthorName = (string)jsonNode["metadata"]["levelAuthorName"];
            buffer.songAuthorName = (string)jsonNode["metadata"]["songAuthorName"];
            buffer.songName = (string)jsonNode["metadata"]["songName"];
            buffer.songSubName = (string)jsonNode["metadata"]["songSubName"];
            buffer.bpm = (float)jsonNode["metadata"]["bpm"];
            buffer.downloads = 0;
            buffer.plays = 0; // These 2 were removed from the API
            buffer.upVotes = (int)jsonNode["stats"]["upvotes"];
            buffer.downVotes = (int)jsonNode["stats"]["downvotes"];
            buffer.rating = (float)jsonNode["stats"]["score"];
            buffer.heat = 0.0F; // TODO: Same as above
            buffer.description = (string)jsonNode["description"];
            buffer._id = (string)jsonNode["id"];
            buffer.key = (string)jsonNode["id"]; // TODO: Same as above
            buffer.name = (string)jsonNode["name"];
            buffer.ownerid = (string)jsonNode["uploader"]["id"];
            buffer.ownerName = (string)jsonNode["uploader"]["name"];
            buffer.hash = (string)(jsonNode["versions"][0]["hash"]);
            buffer.hash = buffer.hash.ToLower();
            buffer.uploaded = (string)jsonNode["uploaded"];
            buffer.downloadURL = (string)(jsonNode["versions"][0]["downloadURL"]);
            buffer.coverURL = (string)(jsonNode["versions"][0]["coverURL"]);
            return buffer;
        }


        public bool Compare(Song compareTo)
        {
            return compareTo.hash == hash;
        }


        public Song(CustomPreviewBeatmapLevel _data)
        {
            songName = _data.songName;
            songSubName = _data.songSubName;
            songAuthorName = _data.songAuthorName;
            levelAuthorName = _data.levelAuthorName;
            path = _data.customLevelPath;
            hash = SongCore.Collections.hashForLevelID(_data.levelID).ToLower();
        }
    }
    [Serializable]
    public class RootObject
    {
        public Song[] songs;
    }
}
