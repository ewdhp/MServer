# Micro Credit Backend

This project is a micro credit backend application built using .NET. It provides functionalities for managing credit applications, including applying for credit and retrieving credit details.

## Project Structure

- **Controllers**
  - `HomeController.cs`: Manages HTTP requests related to the home route.
  
- **Models**
  - `LoanModel.cs`: Defines the data structure for a credit application.

- **Services**
  - `LoanService.cs`: Contains business logic related to credit operations.

- **Data**
  - `UDbContext.cs`: Manages the database connection and entities.

- **MServer.csproj**: Project file containing dependencies and build settings.

- **Program.cs**: Entry point of the application.

- **Startup.cs**: Configures services and the application's request pipeline.

## Setup Instructions

1. Clone the repository:
   ```
   git clone <repository-url>
   ```

2. Navigate to the project directory:
   ```
   cd MicroCreditBackend
   ```

3. Restore the dependencies:
   ```
   dotnet restore
   ```

4. Run the application:
   ```
   dotnet run
   ```

## Usage

- Access the home route to interact with the application.
- Use the provided endpoints to apply for credit and retrieve credit details.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any enhancements or bug fixes.