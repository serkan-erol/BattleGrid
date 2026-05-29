# BattleGrid

	We are almost there! Now, you can actually play the game (with a UI)!
	
	To run the project, you will need .NET 10 SDK and PostgreSQL(v18+) 
		installed on your machine.
		
		IMPORTANT: If you are installing PostgreSQL for the first time, 
			make sure to set the locale setting by hand; 
			choose any language you want. Leaving it as default may cause problems!

	Then, I suggest you set the password for PostgreSQL to 1234 for simplicity. 
		If you choose a different password, make sure to update the connection string 
		in the appsettings.json and appsettings.Development.json files 
		in the BattleGrid.API folder.
	Make sure the port setting for PostgreSQL is 5432, which is the default.
	
	Open the repo with your IDE of choice (Visual Studio is preferred).
	
	First things first, you need to create the database. Open pgAdmin4.
	Create the DB with the exact name "BattleGridDB", under Databases.
	Find the SQL codes for the DB in the BattleGrid.Infrastructure/DatabaseCodes
		Start with 01_Tables_and_Indexed.sql to create the tables.
		Then, run 02_SP_and_Triggers.sql query.
		They are named 01, 02 to indicate the order of execution for Docker.

	Back to the IDE. If you view via solution explorer (in VS),
		You will see there are currently 2 main parts: src and tests.
	The /src contains 2 different projects, 
		the server backend, and a blazor server frontend.
	The /tests contains 2 different projects, 
		unit tests to automate game logic tests and endpoint integration tests,
		and a console app to test game logic manually.


	---------------------------------------------------------------------------------------------------
	
		- Server backend. This is the main part of the project.
		  It will handle all the game logic and communication with clients.
		  It is built using ASP.NET Core. 
		  It uses RESTful APIs for DB related functionality
			and it uses SignalR for real-time communication/handling of user interactions.
		  
		  API, Application, Contracts, Domain, and Infrastructure belong to server backend.

		  Currently, API endpoints include a wide range of functionality. Including but not limited to:
		  - Register as a user
		  - Login as a user/admin
		  - Get all user info or a certain user's info
		  - Get ship type list
		  - Save ship placements in DB
		  - Save match moves in DB
		  - Player stat updates in DB
		  - Update user name, email, password
		  - Admin actions such as 
			- ban/unban a player
			- end/start seasons
		  
		  SignalR hubs are implemented and allows you to:
		  - Join the queue for matchmaking
		  - Play the game, win or lose; that is up to you

		  Game logic is implemented in the backend, in BattleGrid.Domain/GameLogic folder.

		  To run the server:
		  Open a terminal window in your IDE and in the main project folder,
			run the commands in order:
			
			dotnet clean		// To clean the previous build's output; like, binaries
			dotnet build		/* This will build the entire solution, all 4 projects:
								 * Server, Console, Web and Tests;
						 		 *  and restore any necessary packages.
						 		 */
			cd BattleGrid.API	// This will move to the server backend project folder

			dotnet run			/* "dotnet run --launch-profile https" to run the https version
								 * 
								 * Or preferably, choose the BattleGrid.API project 
								 * as the startup project in VS and run it. This way
						 	 	 *  you can choose between https, http or IIS Express.
								 *
								 * *** Also, now there is a LaunchApp profile that runs both
								 *		backend and frontend projects simultaneously! ***
						 	 	 */

		  Running it directly via VS will open a new browser window with 
			the API documentation (Swagger UI) where you can test the API endpoints.
		  If you run the LaunchApp profile via VS, it will open both Swagger UI for backend
			and the frontend page in separate browser windows.

		  If you run it via a `dotnet run` command, only http works. 
			Open your browser and go to http://localhost:4744/swagger
		  Run it with `dotnet run --launch-profile https` to get it working with https.
		    Then, you can go to https://localhost:4743/swagger


	---------------------------------------------------------------------------------------------------

		- Blazor Server (BattleGrid.Web) is a blazor web app with interactive render mode set to server.
		  ***NEW*** Now, you can:
			- Register, login
			- Join the queue to find a match! You will be matched with a suitable opponent 
			   based on your rating and the time elapsed since you joined the queue.
			- After you are matched with an opponent, you will be redirected to the game page
			   where you can finally, actually play the game!


		  While the backend server is running on a terminal, 
		   open a new terminal in the project's main folder.
		  Assuming you followed the instructions to start the server, 
		   Web project is also built and ready.

		  cd BattleGrid.Web
		  dotnet run

		  Then go to http://localhost:4746 and the home page will greet you. 
		    Or run it with `dotnet run --launch-profile https` and
			go to https://localhost:4745

		  Try and test it.

		  Test what happens if you:
			- wait in queue for too long.
			- do not place all of your ships in ship placement phase.
			- click on 'Save placement' without placing any ships
				or after placing only some of your ships.
			- never fire a shot.
			- fire a shot on an already targeted area.
			- fire a shot on your own board.
			- play the game the way it was supposed to be.
			- play the game the way it was not supposed to be.

		  Test everything you can think of!


	---------------------------------------------------------------------------------------------------

		- Console app. It is used to test the game logic. 2 ships per player is pre-placed.
		  Then, starting with Player 1, players will take turns to enter coordinates 
			to attack the opponent's ships.
		  And we control if the game flow is working correctly 
			and if the game end condition is detected properly.

		  For this one, you do not need the server running. It is completely separate from APIs or Hubs.
		  It just uses the BattleGrid.Domain/GameLogic to fire up a game.

		  Open a terminal

			cd BattleGrid.Console	// Moves to the console app project folder
			dotnet run				/* This will launch a new terminal window
									 *  (or will launch it in the current terminal window)
									 *	where you can play the game.
									 */


	---------------------------------------------------------------------------------------------------

		- Tests. Unit tests for the backend. It is built using xUnit.
		  Currently, it only includes tests for game logic. Such as,
		   - Coordinate validation
		   - Ship placement validation
		   - Player turn management
		   - Hit registration
		   - Game end detection
		  
		  It does not include tests for API endpoints, yet. 
		  We may implement it in the future.

		  Open a terminal, and again assuming you built the project while following instructions for
		   server part.

			cd BattleGrid.Tests	// Moves to the test project folder
			dotnet test			/* This will run all the tests in the project
								 *	and show the results in the terminal.
								 */

		  Or, you can open the Test Explorer in VS (Test > Test Explorer) 
			where you can run and debug tests individually or all at once.