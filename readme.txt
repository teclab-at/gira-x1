https://knx-user-forum.de/forum/supportforen/gira-logik-sdk/1537177-husqvarner-automower
https://gira-x1:4433/discovery/log
https://developer.husqvarnagroup.cloud/





curl -X GET https://api.amc.husqvarna.dev/v1/mowers/ -H "Authorization: Bearer <token>" -H "Authorization-Provider: husqvarna" -H "Content-Type: application/vnd.api+json" -H "X-Api-Key: d8daf87d-0d53-4c2d-a75c-b5b2ab1b3ca0"
curl -X POST -d "client_id=d8daf87d-0d53-4c2d-a75c-b5b2ab1b3ca0&grant_type=password&username=automower@teclab.at&password=MyWay040275!" https://api.authentication.husqvarnagroup.dev/v1/oauth2/token