# Docker
## Docker Commands QR
For good measure, complete all of these commands inside of a terminal and verify the current directory is the directory with "**docker-compose.yml**" and "**Dockerfile**" (i.e. C:/FE-BUDDYBot/FEBuddyDiscordBot)

    Display Docker Images:
        docker images
    
    Display All Docker Containers:
        docker ps -a
    
    Build a Docker Image:
        docker build -t "{name}:{version}" .
    
    Build a New Container:
        docker run -d --name {container name} {image name}:{image version}
    
    Update restart policy of a Container:
        docker update --restart=unless-stopped {container name or id}
    
    Stop a Docker Container:
        docker stop {container id}
    
    Remove a Docker Container:
        docker rm {container id}
    
    See the history of an Image:
        docker image history {image Id}
    
    See the docker logs:
        docker logs {container id or name}

<hr>

## Docker-Compose
### Docker Compose Commands QR
    Build / Update Code Changes:
        docker-compose build

    Start Docker instance:
        docker-compose up (In terminal)
        docker-compose up -d (In Background)
    
    Close Docker instance:
        CTRL + C (while in command prompt to exit)
        docker-compose down

### To run the docker instance inside a command prompt
    1) Git Clone the Repository
        - If it is private and you have TFA you will need a Token from GitHub.
        - NOTE: The file "config.json" is not in the GitHub Repo. YOU will need to create it and put in the required configuration items.
    2) Make sure "Docker" is installed on the computer you are running it from.
    3) Open Command Prompt
    4) Navigate to the folder from GitHub
        - Make sure your current directory is the directory with "docker-compose.yml" and "Dockerfile"(i.e. C:/FE-BUDDYBot/FEBuddyDiscordBot)
    5) Enter the Command "docker-compose build"
        - This will build the GitHub repo so it can be ran inside a docker container.
    6) Enter the Command "docker-compose up"
        - This will create the instance inside the command terminal.
        - To stop the container Press "CTRL + C"
        - Enter the Command "docker-compose down" (to verify the service has stopped)


### To run the docker instance in the background
    1) Git Clone the Repository
        - If it is private and you have TFA you will need a Token from GitHub.
        - NOTE: The file "config.json" is not in the GitHub Repo. YOU will need to create it and put in the required configuration items.
    2) Make sure "Docker" is installed on the computer you are running it from.
    3) Open Command Prompt
    4) Navigate to the folder from GitHub
        - Make sure your current directory is the directory with "docker-compose.yml" and "Dockerfile".
        - i.e. C:/FE-BUDDYBot/FEBuddyDiscordBot
    5) Enter the Command "docker-compose build"
        - This will build the GitHub repo so it can be ran inside a docker container.
    6) Enter the Command "docker-compose up -d"
        - This will create the instance in the background.
        - To stop the container Enter the Command "docker-compose down"

<hr>