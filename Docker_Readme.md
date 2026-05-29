First, you do not have to use it!

However, since we have different operating systems, Docker allows us 
 to run the application in a consistent environment across all platforms.

Follow instructions to install Docker on your machine: 
 https://docs.docker.com/get-started

Make sure to make the necessary changes to your computer's settings to
 allow virtualization. Again, Docker docs provide all the necessary info.

Required "Dockerfile" and docker-compose.yml files are already included
 in the project. You do not need to create them. You can modify them to
 fit your needs, but please do not commit any changes to them unless
 it is absolutely necessary.

Since everything is already set up, after installing Docker you will need
to launch the Docker Desktop app to start the Docker Engine. Then you will
need the following commands:

 To build the containers for the first time: `docker compose up --build`
  First build will take some time.
  You will see the output of the build process in the terminal 
   and in the Docker Desktop app. After the build is completed,
   container will be running.

 To build the containers: `docker compose up`
  Useful after a `docker compose down` or `...down -v` command (see below)
   to quickly build containers

 To start the containers: `docker compose start`
  When the container is running you can go to http://localhost:4744/swagger

 To stop the containers: `docker compose stop`
  Just stops containers, does not delete anything

 To delete the containers: `docker compose down`
  Adding the `-v` flag deletes volumes as well

 For whatever reason if you decide to query SQL with psql from a terminal
  instead of using swaggerUI or using pgAdmin4 to query the DB:
 `docker exec -it battlegrid_db psql -U postgres -d BattleGridDB`

 For extra help: `docker help` or `docker --help`
				 `docker compose --help`
				 `docker compose down --help` etc.

 Also, you can do start-stop via the Docker Desktop app.

Current docker-compose.yml is set to create a separate DB with the same,
 original name, BattleGridDB, on port 5433. Since, default port in pgAdmin 
 is 5432, you won't be able to see this new DB in pgAdmin4. Consequently, 
 you won't be able to see any changes you made to it. Don't worry, it is there.
It has its own container and image, and included in the volume. If you 
 want to see the DB used by this container, you need to register a new server 
 with the port 5433 in pgAdmin4. It is a simple, straightforward process. 
 Just make sure to register this new server while the container is running! 
 If not,  pgAdmin4 can not find a server on port 5433 and will prompt you 
 with an error and ask for your password.

To register a new server in pgAdmin4:
 -Right click on the "Servers"
 -Register->Server
 -Enter a name of your choosing
 -Move to the "Connection" tab up top
 -Enter "localhost" for Host name/address
 -Change the port to 5433
 save it, and that is it. 
 
 In this new server you can see the version of BattleGridDB,
  that the container we created with docker, uses. Any change made in this DB
  has no effect on the original DB. 
 For example, when you register a new user via swaggerUI 
 (ofcourse, while the containers are running) you can see this new user is added 
  to the container's DB.