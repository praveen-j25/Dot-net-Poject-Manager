using MySql.Data.MySqlClient;

namespace TaskManagerMVC.Data;

public class DatabaseSeeder
{
    private readonly DbConnectionFactory _dbFactory;

    public DatabaseSeeder(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public void Initialize()
    {
        using var conn = _dbFactory.CreateConnection();
        
        int maxRetries = 30;
        int currentRetry = 0;
        bool connected = false;

        while (currentRetry < maxRetries && !connected)
        {
            try
            {
                Console.WriteLine($"[MIGRATION] Attempting to connect to database... (Attempt {currentRetry + 1}/{maxRetries})");
                conn.Open();
                connected = true;
                Console.WriteLine("[MIGRATION] Successfully connected to database.");
            }
            catch (MySqlException ex)
            {
                currentRetry++;
                Console.WriteLine($"[MIGRATION WARNING] Database connection failed: {ex.Message}");
                if (currentRetry >= maxRetries)
                {
                    Console.WriteLine("[MIGRATION ERROR] Could not connect to the database after multiple attempts.");
                    throw;
                }
                Console.WriteLine("[MIGRATION] Waiting 5 seconds before retrying...");
                System.Threading.Thread.Sleep(5000);
            }
        }

        // Check if tables exist - if not, create the full schema
        if (!TableExists(conn, "roles"))
        {
            Console.WriteLine("[MIGRATION] Fresh database detected. Creating all tables...");
            CreateAllTables(conn);
            SeedDefaultData(conn);
            Console.WriteLine("[MIGRATION] Database setup complete!");
        }
        else
        {
            Console.WriteLine("[MIGRATION] Existing database detected. Running migrations...");
        }

        // Run incremental migrations
        RunMigrations(conn);
    }

    private bool TableExists(MySqlConnection conn, string tableName)
    {
        var sql = @"SELECT COUNT(*) FROM information_schema.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@table", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private void CreateAllTables(MySqlConnection conn)
    {
        var createTablesSql = @"
            -- LOOKUP TABLES
            CREATE TABLE IF NOT EXISTS roles (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(50) NOT NULL UNIQUE,
                description VARCHAR(255) DEFAULT NULL,
                permissions JSON DEFAULT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS statuses (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(50) NOT NULL UNIQUE,
                display_name VARCHAR(100) NOT NULL,
                color VARCHAR(20) DEFAULT '#6c757d',
                sort_order INT DEFAULT 0,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS priorities (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(50) NOT NULL UNIQUE,
                display_name VARCHAR(100) NOT NULL,
                color VARCHAR(20) DEFAULT '#6c757d',
                level INT DEFAULT 0,
                sort_order INT DEFAULT 0,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS categories (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(100) NOT NULL UNIQUE,
                description VARCHAR(255) DEFAULT NULL,
                color VARCHAR(20) DEFAULT '#6c757d',
                icon VARCHAR(50) DEFAULT 'bi-folder',
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;

            -- ORGANIZATION TABLES
            CREATE TABLE IF NOT EXISTS departments (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(100) NOT NULL UNIQUE,
                description VARCHAR(255) DEFAULT NULL,
                manager_id INT DEFAULT NULL,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS teams (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(100) NOT NULL,
                description VARCHAR(255) DEFAULT NULL,
                department_id INT DEFAULT NULL,
                team_lead_id INT DEFAULT NULL,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS users (
                id INT PRIMARY KEY AUTO_INCREMENT,
                first_name VARCHAR(100) NOT NULL,
                last_name VARCHAR(100) NOT NULL,
                email VARCHAR(255) NOT NULL UNIQUE,
                password VARCHAR(255) NOT NULL,
                phone VARCHAR(20) DEFAULT NULL,
                role_id INT DEFAULT 3,
                department_id INT DEFAULT NULL,
                team_id INT DEFAULT NULL,
                job_title VARCHAR(100) DEFAULT NULL,
                employee_id VARCHAR(50) DEFAULT NULL UNIQUE,
                reports_to INT DEFAULT NULL,
                hire_date DATE DEFAULT NULL,
                profile_image VARCHAR(500) DEFAULT NULL,
                profile_image_content LONGBLOB,
                profile_image_type VARCHAR(100),
                is_active BOOLEAN DEFAULT TRUE,
                is_verified BOOLEAN DEFAULT TRUE,
                email_verified_at TIMESTAMP NULL,
                last_login TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE SET NULL,
                FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
                FOREIGN KEY (team_id) REFERENCES teams(id) ON DELETE SET NULL,
                FOREIGN KEY (reports_to) REFERENCES users(id) ON DELETE SET NULL,
                INDEX idx_email (email),
                INDEX idx_role (role_id),
                INDEX idx_department (department_id),
                INDEX idx_active (is_active)
            ) ENGINE=InnoDB;
        ";

        ExecuteMultiStatement(conn, createTablesSql);

        // Add FK references that require users table
        ExecuteSafely(conn, "ALTER TABLE departments ADD FOREIGN KEY (manager_id) REFERENCES users(id) ON DELETE SET NULL");
        ExecuteSafely(conn, "ALTER TABLE teams ADD FOREIGN KEY (team_lead_id) REFERENCES users(id) ON DELETE SET NULL");

        // Projects
        var projectTablesSql = @"
            CREATE TABLE IF NOT EXISTS projects (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(200) NOT NULL,
                description TEXT DEFAULT NULL,
                code VARCHAR(20) DEFAULT NULL UNIQUE,
                department_id INT DEFAULT NULL,
                manager_id INT DEFAULT NULL,
                start_date DATE DEFAULT NULL,
                end_date DATE DEFAULT NULL,
                budget DECIMAL(15,2) DEFAULT 0,
                status ENUM('planning', 'active', 'on_hold', 'completed', 'cancelled') DEFAULT 'planning',
                priority ENUM('low', 'medium', 'high', 'critical') DEFAULT 'medium',
                is_active BOOLEAN DEFAULT TRUE,
                created_by INT DEFAULT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
                FOREIGN KEY (manager_id) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
                INDEX idx_status (status),
                INDEX idx_manager (manager_id)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS project_members (
                id INT PRIMARY KEY AUTO_INCREMENT,
                project_id INT NOT NULL,
                user_id INT NOT NULL,
                role VARCHAR(50) DEFAULT 'member',
                joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                UNIQUE KEY unique_project_user (project_id, user_id),
                INDEX idx_project (project_id),
                INDEX idx_user (user_id)
            ) ENGINE=InnoDB;
        ";

        ExecuteMultiStatement(conn, projectTablesSql);

        // Tasks
        var taskTablesSql = @"
            CREATE TABLE IF NOT EXISTS tasks (
                id INT PRIMARY KEY AUTO_INCREMENT,
                title VARCHAR(255) NOT NULL,
                description TEXT DEFAULT NULL,
                project_id INT DEFAULT NULL,
                category_id INT DEFAULT NULL,
                priority_id INT DEFAULT 2,
                status_id INT DEFAULT 1,
                task_type VARCHAR(50) DEFAULT 'task',
                assigned_to INT DEFAULT NULL,
                assigned_by INT DEFAULT NULL,
                assigned_at TIMESTAMP NULL,
                created_by INT DEFAULT NULL,
                start_date DATE DEFAULT NULL,
                due_date DATE DEFAULT NULL,
                completed_date TIMESTAMP NULL,
                progress INT DEFAULT 0,
                estimated_hours DECIMAL(5,2) DEFAULT 0,
                actual_hours DECIMAL(5,2) DEFAULT 0,
                hourly_rate DECIMAL(10,2) DEFAULT 0.00,
                is_billable BOOLEAN DEFAULT TRUE,
                tags VARCHAR(500) DEFAULT NULL,
                parent_task_id INT DEFAULT NULL,
                attachments_count INT DEFAULT 0,
                comments_count INT DEFAULT 0,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE SET NULL,
                FOREIGN KEY (priority_id) REFERENCES priorities(id) ON DELETE SET NULL,
                FOREIGN KEY (status_id) REFERENCES statuses(id) ON DELETE SET NULL,
                FOREIGN KEY (assigned_to) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (assigned_by) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
                FOREIGN KEY (parent_task_id) REFERENCES tasks(id) ON DELETE SET NULL,
                INDEX idx_assigned_to (assigned_to),
                INDEX idx_created_by (created_by),
                INDEX idx_status (status_id),
                INDEX idx_priority (priority_id),
                INDEX idx_due_date (due_date),
                INDEX idx_project (project_id),
                INDEX idx_category (category_id)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS task_assignees (
                id INT PRIMARY KEY AUTO_INCREMENT,
                task_id INT NOT NULL,
                user_id INT NOT NULL,
                assigned_by INT NOT NULL,
                assigned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                FOREIGN KEY (assigned_by) REFERENCES users(id) ON DELETE CASCADE,
                UNIQUE KEY unique_task_user (task_id, user_id),
                INDEX idx_task (task_id),
                INDEX idx_user (user_id)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS task_comments (
                id INT PRIMARY KEY AUTO_INCREMENT,
                task_id INT NOT NULL,
                user_id INT NOT NULL,
                comment TEXT NOT NULL,
                comment_type VARCHAR(50) DEFAULT 'comment',
                is_internal BOOLEAN DEFAULT FALSE,
                parent_comment_id INT DEFAULT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                FOREIGN KEY (parent_comment_id) REFERENCES task_comments(id) ON DELETE CASCADE,
                INDEX idx_task (task_id),
                INDEX idx_user (user_id),
                INDEX idx_parent (parent_comment_id)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS task_attachments (
                id INT PRIMARY KEY AUTO_INCREMENT,
                task_id INT NOT NULL,
                uploaded_by INT NOT NULL,
                file_name VARCHAR(255) NOT NULL,
                original_name VARCHAR(255) NOT NULL,
                file_type VARCHAR(100) DEFAULT NULL,
                file_size INT DEFAULT 0,
                file_path VARCHAR(500) DEFAULT 'BLOB_STORAGE',
                file_content LONGBLOB,
                content_type VARCHAR(100),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (uploaded_by) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_task (task_id)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS task_activity_log (
                id INT PRIMARY KEY AUTO_INCREMENT,
                task_id INT NOT NULL,
                user_id INT NOT NULL,
                action VARCHAR(100) NOT NULL,
                field_changed VARCHAR(100) DEFAULT NULL,
                old_value TEXT DEFAULT NULL,
                new_value TEXT DEFAULT NULL,
                ip_address VARCHAR(45) DEFAULT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_task (task_id),
                INDEX idx_user (user_id),
                INDEX idx_created (created_at)
            ) ENGINE=InnoDB;
        ";

        ExecuteMultiStatement(conn, taskTablesSql);

        // Time logs, notifications, activity logs, password resets
        var remainingTablesSql = @"
            CREATE TABLE IF NOT EXISTS time_logs (
                id INT PRIMARY KEY AUTO_INCREMENT,
                task_id INT NOT NULL,
                user_id INT NOT NULL,
                project_id INT DEFAULT NULL,
                hours_logged DECIMAL(5,2) NOT NULL,
                log_date DATE NOT NULL,
                description TEXT DEFAULT NULL,
                is_billable BOOLEAN DEFAULT TRUE,
                is_approved BOOLEAN DEFAULT FALSE,
                approved_by INT DEFAULT NULL,
                approved_at TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL,
                FOREIGN KEY (approved_by) REFERENCES users(id) ON DELETE SET NULL,
                INDEX idx_task (task_id),
                INDEX idx_user (user_id),
                INDEX idx_project (project_id),
                INDEX idx_date (log_date)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS notifications (
                id INT PRIMARY KEY AUTO_INCREMENT,
                user_id INT NOT NULL,
                title VARCHAR(200) NOT NULL,
                message TEXT DEFAULT NULL,
                type VARCHAR(50) DEFAULT 'info',
                reference_type VARCHAR(50) DEFAULT NULL,
                reference_id INT DEFAULT NULL,
                is_read BOOLEAN DEFAULT FALSE,
                read_at TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_user_read (user_id, is_read),
                INDEX idx_created (created_at)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS activity_logs (
                id INT PRIMARY KEY AUTO_INCREMENT,
                user_id INT NOT NULL,
                action VARCHAR(100) NOT NULL,
                entity_type VARCHAR(50) DEFAULT NULL,
                entity_id INT DEFAULT NULL,
                description TEXT DEFAULT NULL,
                ip_address VARCHAR(45) DEFAULT NULL,
                user_agent VARCHAR(500) DEFAULT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_user (user_id),
                INDEX idx_entity (entity_type, entity_id),
                INDEX idx_created (created_at)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS password_resets (
                id INT PRIMARY KEY AUTO_INCREMENT,
                user_id INT NOT NULL,
                reset_token VARCHAR(255) NOT NULL UNIQUE,
                token_expiry TIMESTAMP NOT NULL,
                is_used BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_token (reset_token),
                INDEX idx_expiry (token_expiry)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS pending_users (
                id INT PRIMARY KEY AUTO_INCREMENT,
                first_name VARCHAR(100) NOT NULL,
                last_name VARCHAR(100) NOT NULL,
                email VARCHAR(255) NOT NULL UNIQUE,
                password VARCHAR(255) NOT NULL,
                phone VARCHAR(20) DEFAULT NULL,
                job_title VARCHAR(100) DEFAULT NULL,
                department_id INT DEFAULT NULL,
                status VARCHAR(20) DEFAULT 'pending',
                rejection_reason TEXT DEFAULT NULL,
                reviewed_by INT DEFAULT NULL,
                reviewed_at TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
                FOREIGN KEY (reviewed_by) REFERENCES users(id) ON DELETE SET NULL,
                INDEX idx_status (status),
                INDEX idx_email (email)
            ) ENGINE=InnoDB;
        ";

        ExecuteMultiStatement(conn, remainingTablesSql);
        Console.WriteLine("[MIGRATION] All tables created successfully.");
    }

    private void SeedDefaultData(MySqlConnection conn)
    {
        // Roles
        ExecuteSafely(conn, @"INSERT IGNORE INTO roles (id, name, description, permissions) VALUES
            (1, 'Admin', 'System Administrator with full access', '{""users"": [""create"", ""read"", ""update"", ""delete""], ""projects"": [""create"", ""read"", ""update"", ""delete""], ""tasks"": [""create"", ""read"", ""update"", ""delete"", ""assign""], ""reports"": [""view"", ""export""], ""settings"": [""manage""]}'),
            (2, 'Manager', 'Project Manager with team management access', '{""users"": [""read""], ""projects"": [""create"", ""read"", ""update""], ""tasks"": [""create"", ""read"", ""update"", ""assign""], ""reports"": [""view""]}'),
            (3, 'Employee', 'Regular employee with task access', '{""tasks"": [""read"", ""update""], ""time_logs"": [""create"", ""read""]}')");

        // Statuses
        ExecuteSafely(conn, @"INSERT IGNORE INTO statuses (name, display_name, color, sort_order) VALUES
            ('pending', 'Pending', '#ffc107', 1),
            ('assigned', 'Assigned', '#17a2b8', 2),
            ('in_progress', 'In Progress', '#0d6efd', 3),
            ('in_review', 'In Review', '#6f42c1', 4),
            ('testing', 'Testing', '#fd7e14', 5),
            ('blocked', 'Blocked', '#dc3545', 6),
            ('completed', 'Completed', '#198754', 7),
            ('cancelled', 'Cancelled', '#adb5bd', 8)");

        // Priorities
        ExecuteSafely(conn, @"INSERT IGNORE INTO priorities (name, display_name, color, level, sort_order) VALUES
            ('low', 'Low', '#28a745', 1, 1),
            ('medium', 'Medium', '#ffc107', 2, 2),
            ('high', 'High', '#fd7e14', 3, 3),
            ('critical', 'Critical', '#dc3545', 4, 4)");

        // Categories
        ExecuteSafely(conn, @"INSERT IGNORE INTO categories (name, description, color, icon) VALUES
            ('Development', 'Software development tasks', '#0d6efd', 'bi-code-slash'),
            ('Bug Fix', 'Bug fixes and issues', '#dc3545', 'bi-bug'),
            ('Feature', 'New feature development', '#198754', 'bi-star'),
            ('Documentation', 'Documentation tasks', '#6c757d', 'bi-file-text'),
            ('Testing', 'Testing and QA tasks', '#fd7e14', 'bi-check-circle'),
            ('Design', 'UI/UX design tasks', '#6f42c1', 'bi-palette'),
            ('Support', 'Customer support tasks', '#17a2b8', 'bi-headset')");

        // Departments
        ExecuteSafely(conn, @"INSERT IGNORE INTO departments (id, name, description) VALUES
            (1, 'Development', 'Software Development Team'),
            (2, 'QA', 'Quality Assurance Team'),
            (3, 'Design', 'UI/UX Design Team'),
            (4, 'DevOps', 'DevOps and Infrastructure Team'),
            (5, 'Marketing', 'Marketing and Sales Team'),
            (6, 'HR', 'Human Resources'),
            (7, 'Finance', 'Finance and Accounting')");

        // Teams
        ExecuteSafely(conn, @"INSERT IGNORE INTO teams (id, name, description, department_id) VALUES
            (1, 'Backend Team', 'Backend development team', 1),
            (2, 'Frontend Team', 'Frontend development team', 1),
            (3, 'Mobile Team', 'Mobile app development team', 1),
            (4, 'QA Team', 'Quality assurance team', 2),
            (5, 'UI Team', 'User interface design team', 3),
            (6, 'UX Team', 'User experience design team', 3)");

        // Default Admin User (admin@taskmanager.com / Admin@123)
        ExecuteSafely(conn, @"INSERT IGNORE INTO users (first_name, last_name, email, password, role_id, is_verified, is_active, employee_id, job_title, hire_date) VALUES
            ('System', 'Admin', 'admin@taskmanager.com', '$2a$11$vI8aWBnW3fID.ZQ4/zo1G.q1lRps.9cGLcZEiGDMVr5yUP1KUOYTa', 1, TRUE, TRUE, 'ADM001', 'System Administrator', CURDATE())");

        Console.WriteLine("[MIGRATION] Default data seeded successfully.");
        Console.WriteLine("[MIGRATION] Admin login: admin@taskmanager.com / Admin@123");
    }

    private void RunMigrations(MySqlConnection conn)
    {
        // Migration 1: Add hourly_rate column to tasks if missing
        if (TableExists(conn, "tasks") && !ColumnExists(conn, "tasks", "hourly_rate"))
        {
            ExecuteSafely(conn, "ALTER TABLE tasks ADD COLUMN hourly_rate DECIMAL(10,2) DEFAULT 0.00");
            Console.WriteLine("[MIGRATION] Added hourly_rate column to tasks table.");
        }

        // Migration 2: Ensure notifications type column is wide enough
        if (TableExists(conn, "notifications"))
        {
            var checkNotifSql = @"SELECT CHARACTER_MAXIMUM_LENGTH 
                                  FROM information_schema.COLUMNS 
                                  WHERE TABLE_SCHEMA = DATABASE() 
                                  AND TABLE_NAME = 'notifications' 
                                  AND COLUMN_NAME = 'type'";
            using var checkNotifCmd = new MySqlCommand(checkNotifSql, conn);
            object? result = checkNotifCmd.ExecuteScalar();
            long length = 0;
            if (result != null && result != DBNull.Value)
            {
                length = Convert.ToInt64(result);
            }

            if (length < 50)
            {
                ExecuteSafely(conn, "ALTER TABLE notifications MODIFY COLUMN type VARCHAR(50) NOT NULL DEFAULT 'info'");
                Console.WriteLine("[MIGRATION] Expanded type column in notifications table.");
            }
        }

        // Migration 3: Ensure pending_users table exists
        if (!TableExists(conn, "pending_users"))
        {
            ExecuteSafely(conn, @"CREATE TABLE IF NOT EXISTS pending_users (
                id INT PRIMARY KEY AUTO_INCREMENT,
                first_name VARCHAR(100) NOT NULL,
                last_name VARCHAR(100) NOT NULL,
                email VARCHAR(255) NOT NULL UNIQUE,
                password VARCHAR(255) NOT NULL,
                phone VARCHAR(20) DEFAULT NULL,
                job_title VARCHAR(100) DEFAULT NULL,
                department_id INT DEFAULT NULL,
                status VARCHAR(20) DEFAULT 'pending',
                rejection_reason TEXT DEFAULT NULL,
                reviewed_by INT DEFAULT NULL,
                reviewed_at TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
                FOREIGN KEY (reviewed_by) REFERENCES users(id) ON DELETE SET NULL,
                INDEX idx_status (status),
                INDEX idx_email (email)
            ) ENGINE=InnoDB");
            Console.WriteLine("[MIGRATION] Created pending_users table.");
        }
    }

    private bool ColumnExists(MySqlConnection conn, string tableName, string columnName)
    {
        var sql = @"SELECT COUNT(*) FROM information_schema.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND TABLE_NAME = @table AND COLUMN_NAME = @column";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@column", columnName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private void ExecuteMultiStatement(MySqlConnection conn, string sql)
    {
        // Split on semicolons and execute each statement
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var statement in statements)
        {
            if (!string.IsNullOrWhiteSpace(statement) && !statement.StartsWith("--"))
            {
                try
                {
                    using var cmd = new MySqlCommand(statement, conn);
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException ex)
                {
                    Console.WriteLine($"[MIGRATION WARNING] {ex.Message}");
                }
            }
        }
    }

    private void ExecuteSafely(MySqlConnection conn, string sql)
    {
        try
        {
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
        catch (MySqlException ex)
        {
            Console.WriteLine($"[MIGRATION WARNING] {ex.Message}");
        }
    }
}
